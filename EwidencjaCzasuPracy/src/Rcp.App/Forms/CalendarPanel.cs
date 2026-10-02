using System.Globalization;
using Rcp.App.Controls;
using Rcp.Core;
using Rcp.Core.Services;

namespace Rcp.App.Forms;

/// <summary>Kalendarz pracownika: miesiąc, podsumowanie, zdarzenia i nieobecności wybranego dnia.</summary>
internal sealed class CalendarPanel : UserControl
{
    private readonly AppHost _host;
    private readonly EmployeePicker _picker;
    private readonly MonthView _month = new() { Dock = DockStyle.Fill };
    private readonly Label _title = new() { Dock = DockStyle.Top, Height = 34, Font = new Font("Segoe UI", 14f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _monthLabel = new() { AutoSize = true, MinimumSize = new Size(170, 0), Margin = new Padding(3, 8, 3, 3), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
    private readonly Label _summary = new() { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(6), BackColor = Ui.MutedColor };
    private readonly Label _dayInfo = new() { Dock = DockStyle.Top, Height = 92, Padding = new Padding(4) };
    private readonly ListView _dayEvents = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
    private Employee? _employee;
    private PeriodSummary? _period;

    public CalendarPanel(AppHost host)
    {
        _host = host;
        _picker = new EmployeePicker(host.Ctx) { Dock = DockStyle.Fill, IncludeInactive = true };
        _picker.SelectionChanged += e => { if (e != null) SetEmployee(e); };

        _dayEvents.Columns.Add("Godzina", 75);
        _dayEvents.Columns.Add("Kierunek", 75);
        _dayEvents.Columns.Add("Czytnik / źródło", 140);
        _dayEvents.Columns.Add("Uwagi", 200);
        _dayEvents.DoubleClick += (_, _) => EditEvent();

        var nav = Ui.Toolbar(
            Ui.Button("<", (_, _) => ShiftMonth(-1), 40),
            _monthLabel,
            Ui.Button(">", (_, _) => ShiftMonth(1), 40),
            Ui.Button("Dziś", (_, _) => { _month.Selected = DateOnly.FromDateTime(DateTime.Today); RefreshData(); }),
            Ui.Button("Eksport miesiąca CSV…", (_, _) => ExportMonth()),
            Ui.Button("Drukuj kartę ewidencji…", (_, _) => PrintMonth()));

        var center = new Panel { Dock = DockStyle.Fill };
        center.Controls.Add(_month);
        center.Controls.Add(_summary);
        center.Controls.Add(nav);
        center.Controls.Add(_title);

        var dayBox = new GroupBox { Text = "Wybrany dzień", Dock = DockStyle.Fill };
        var dayButtons = Ui.Toolbar(
            Ui.Button("Dodaj wejście/wyjście…", (_, _) => AddEvent()),
            Ui.Button("Koryguj…", (_, _) => EditEvent()),
            Ui.Button("Usuń", (_, _) => DeleteEvent()),
            Ui.Button("Nieobecność…", (_, _) => AddAbsence()),
            Ui.Button("Usuń nieobecność", (_, _) => DeleteAbsence()));
        dayButtons.Dock = DockStyle.Bottom;
        dayBox.Controls.Add(_dayEvents);
        dayBox.Controls.Add(_dayInfo);
        dayBox.Controls.Add(dayButtons);

        var left = new GroupBox { Text = "Pracownik", Dock = DockStyle.Fill };
        left.Controls.Add(_picker);

        var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        right.Panel1.Controls.Add(center);
        right.Panel2.Controls.Add(dayBox);

        var main = new SplitContainer { Dock = DockStyle.Fill };
        main.Panel1.Controls.Add(left);
        main.Panel2.Controls.Add(right);
        Controls.Add(main);

        Load += (_, _) =>
        {
            try
            {
                main.SplitterDistance = 260;
                right.SplitterDistance = Math.Max(300, right.Width - 420);
            }
            catch (Exception) { /* za małe okno – zostaw domyślny podział */ }
        };

        _month.DateSelected += _ => ShowDay();
        _month.DateActivated += _ => AddEvent();
        host.Scanned += r => { if (_employee != null && r.Employee?.Id == _employee.Id) RefreshData(); };
        host.DataChanged += () => { if (_employee != null) RefreshData(); };
        UpdateMonthLabel();
        ShowDay();
    }

    public void SetEmployee(Employee e)
    {
        _employee = e;
        _title.Text = $"{e.FullName}  •  nr {e.EmployeeNo}" + (e.DepartmentName is { } d ? $"  •  {d}" : "") + (e.Active ? "" : "  (nieaktywny)");
        RefreshData();
    }

    public void ShowEmployee(Employee e)
    {
        _picker.Select(e);
        SetEmployee(e);
    }

    private void ShiftMonth(int delta)
    {
        var m = _month.Month.AddMonths(delta);
        var day = Math.Min(_month.Selected.Day, DateTime.DaysInMonth(m.Year, m.Month));
        _month.Selected = new DateOnly(m.Year, m.Month, day);
        RefreshData();
    }

    private void UpdateMonthLabel()
        => _monthLabel.Text = _month.Month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

    private void RefreshData()
    {
        UpdateMonthLabel();
        if (_employee == null)
        {
            _month.SetData(Array.Empty<DaySummary>());
            _summary.Text = "Wybierz pracownika z listy po lewej.";
            return;
        }
        var from = _month.Month;
        var to = from.AddMonths(1).AddDays(-1);
        _period = _host.Reports.ForEmployee(_employee, from, to, DateTime.Now);
        _month.SetData(_period.Days);

        var p = _period;
        var absences = string.Join(", ", p.AbsenceDays.Select(kv => $"{AbsenceTypes.Code(kv.Key)}: {kv.Value}"));
        _summary.Text =
            $"Przepracowano: {WorkTimeCalculator.FormatMinutes(p.WorkedMinutes)}   Norma: {WorkTimeCalculator.FormatMinutes(p.NormMinutes)}   " +
            $"Nadgodziny: {WorkTimeCalculator.FormatMinutes(p.OvertimeMinutes)}   Niedopracowanie: {WorkTimeCalculator.FormatMinutes(p.ShortfallMinutes)}\n" +
            $"Dni obecności: {p.DaysPresent}   Nieobecności: {(absences.Length > 0 ? absences : "brak")}   Anomalie: {p.AnomalyCount}";
        _summary.ForeColor = p.AnomalyCount > 0 ? Color.Firebrick : ForeColor;
        ShowDay();
    }

    private void ShowDay()
    {
        var date = _month.Selected;
        if (_period != null && (date < _period.Days[0].Date || date > _period.Days[^1].Date))
        {
            RefreshData();
            return;
        }
        _dayEvents.Items.Clear();
        if (_employee == null)
        {
            _dayInfo.Text = date.ToString("dddd, d MMMM yyyy");
            return;
        }
        var day = _period?.Days.FirstOrDefault(d => d.Date == date);
        _dayInfo.Text = day != null ? MonthView.Describe(day) : date.ToString("dddd, d MMMM yyyy");

        var start = date.ToDateTime(TimeOnly.MinValue);
        foreach (var e in _host.Ctx.Events.ForEmployee(_employee.Id, start, start.AddDays(1)))
        {
            _dayEvents.Items.Add(new ListViewItem(new[]
            {
                e.Timestamp.ToString("HH:mm:ss"), Ui.Dir(e.Direction),
                e.Source == EventSource.Manual ? "wpis ręczny" : e.ReaderName ?? "", e.Note ?? "",
            })
            {
                Tag = e,
                BackColor = e.Source == EventSource.Manual ? Ui.WarnColor : e.Direction == Direction.In ? Ui.InColor : Ui.OutColor,
            });
        }
    }

    private AttendanceEvent? SelectedEvent => _dayEvents.SelectedItems.Count > 0 ? _dayEvents.SelectedItems[0].Tag as AttendanceEvent : null;

    private void AddEvent()
    {
        if (_employee == null) return;
        var last = (_dayEvents.Items.Count > 0 ? _dayEvents.Items[^1].Tag as AttendanceEvent : null);
        var evt = new AttendanceEvent
        {
            Timestamp = _month.Selected.ToDateTime(last == null ? new TimeOnly(8, 0) : new TimeOnly(16, 0)),
            Direction = last?.Direction == Direction.In ? Direction.Out : Direction.In,
        };
        if (Dialogs.EditEvent(FindForm()!, _host.Ctx, evt, _employee)) _host.NotifyDataChanged();
    }

    private void EditEvent()
    {
        if (SelectedEvent is { } e && Dialogs.EditEvent(FindForm()!, _host.Ctx, e, _employee)) _host.NotifyDataChanged();
    }

    private void DeleteEvent()
    {
        if (SelectedEvent is not { } e) return;
        if (!Ui.Confirm($"Usunąć zdarzenie {Ui.Dir(e.Direction).ToLower()} {e.Timestamp:dd.MM.yyyy HH:mm:ss}?")) return;
        _host.Ctx.Events.Delete(e.Id);
        _host.Ctx.Audit.Log("Usunięcie zdarzenia", $"{_employee?.EmployeeNo} {e.Timestamp:yyyy-MM-dd HH:mm:ss} {e.Direction} karta={e.CardUid}");
        _host.NotifyDataChanged();
    }

    private void AddAbsence()
    {
        if (_employee == null) return;
        var day = _period?.Days.FirstOrDefault(d => d.Date == _month.Selected);
        var a = day?.Absence ?? new Absence { DateFrom = _month.Selected, DateTo = _month.Selected, Type = AbsenceType.Urlop };
        if (Dialogs.EditAbsence(FindForm()!, _host.Ctx, a, _employee)) _host.NotifyDataChanged();
    }

    private void DeleteAbsence()
    {
        var day = _period?.Days.FirstOrDefault(d => d.Date == _month.Selected);
        if (day?.Absence is not { } a) { Ui.Info("W wybranym dniu nie ma nieobecności."); return; }
        if (!Ui.Confirm($"Usunąć nieobecność „{AbsenceTypes.Name(a.Type)}” {a.DateFrom:dd.MM}–{a.DateTo:dd.MM.yyyy}?")) return;
        _host.Ctx.Absences.Delete(a.Id);
        _host.Ctx.Audit.Log("Usunięcie nieobecności", $"{_employee?.EmployeeNo} {a.Type} {a.DateFrom}..{a.DateTo}");
        _host.NotifyDataChanged();
    }

    private void ExportMonth()
    {
        if (_employee == null || _period == null) return;
        if (Ui.SaveFile($"ewidencja-{_employee.EmployeeNo}-{_month.Month:yyyy-MM}.csv") is not { } path) return;
        Ui.Try(() =>
        {
            Csv.Write(path,
                new[] { "data", "dzien", "wejscie", "wyjscie", "przepracowano_h", "norma_h", "nadgodziny_h", "nieobecnosc", "swieto", "uwagi" },
                _period.Days, d => new object?[]
                {
                    d.Date, d.Date.ToString("ddd"), d.FirstIn?.ToString("HH:mm"), d.LastOut?.ToString("HH:mm"),
                    Dialogs.FormatHours(d.WorkedMinutes), Dialogs.FormatHours(d.NormMinutes), Dialogs.FormatHours(d.OvertimeMinutes),
                    d.Absence is { } a ? AbsenceTypes.Code(a.Type) : null, d.DayOffName, string.Join("; ", d.Anomalies),
                });
            Ui.Info($"Zapisano: {path}");
        });
    }

    private void PrintMonth()
    {
        if (_employee == null || _period == null) return;
        var doc = new MonthlyTimesheetDocument(_host.Ctx.Settings.CompanyName, _employee, _month.Month, _period);
        using var preview = new PrintPreviewDialog { Document = doc, Width = 900, Height = 1000, UseAntiAlias = true };
        preview.ShowDialog(FindForm());
    }
}
