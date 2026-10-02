using Rcp.App.Controls;
using Rcp.Core;
using Rcp.Core.Services;

namespace Rcp.App.Forms;

/// <summary>Zestawienia zbiorcze czasu pracy (miesięczne / dowolny okres).</summary>
internal sealed class ReportsPanel : UserControl
{
    private readonly AppHost _host;
    private readonly VirtualGrid<EmployeeMonthRow> _grid = new();
    private readonly DateTimePicker _from = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly DateTimePicker _to = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly ComboBox _dept = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly TextBox _search = new() { Width = 180, PlaceholderText = "Pracownik (opcjonalnie)" };
    private readonly CheckBox _onlyAnomalies = new() { Text = "Tylko z anomaliami", AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
    private readonly Button _run;
    private readonly Label _status = Ui.Label("");
    private List<EmployeeMonthRow> _rows = new();
    private CancellationTokenSource? _cts;

    public event Action<Employee>? ShowEmployee;

    public ReportsPanel(AppHost host)
    {
        _host = host;
        SetMonth(0);

        static string H(int m) => WorkTimeCalculator.FormatMinutes(m);
        int Abs(EmployeeMonthRow r, params AbsenceType[] t) => r.Summary.AbsenceDays.Where(kv => t.Contains(kv.Key)).Sum(kv => kv.Value);

        _grid.Column("Nr", r => r.Employee.EmployeeNo, 70)
             .Column("Pracownik", r => r.Employee.FullName, 200)
             .Column("Dział", r => r.Employee.DepartmentName, 130)
             .Column("Dni obecn.", r => r.Summary.DaysPresent, 70)
             .Column("Przepracowano", r => H(r.Summary.WorkedMinutes), 95)
             .Column("Norma", r => H(r.Summary.NormMinutes), 70)
             .Column("Nadgodziny", r => H(r.Summary.OvertimeMinutes), 80)
             .Column("Niedopracowanie", r => H(r.Summary.ShortfallMinutes), 100)
             .Column("Urlop", r => Abs(r, AbsenceType.Urlop, AbsenceType.UrlopNaZadanie, AbsenceType.UrlopOkolicznosciowy), 55)
             .Column("L4", r => Abs(r, AbsenceType.L4, AbsenceType.Opieka), 45)
             .Column("Inne", r => Abs(r, AbsenceType.Delegacja, AbsenceType.UrlopBezplatny, AbsenceType.Inne), 45)
             .Column("Anomalie", r => r.Summary.AnomalyCount, 65, fill: true);
        _grid.RowColor = r => r.Summary.AnomalyCount > 0 ? Ui.WarnColor : null;
        _grid.ItemActivated += r => ShowEmployee?.Invoke(r.Employee);

        _run = Ui.Button("Generuj", async (_, _) => await Generate());
        Controls.Add(_grid.Grid);
        Controls.Add(Ui.Toolbar(
            Ui.Button("Poprzedni miesiąc", (_, _) => SetMonth(-1)),
            Ui.Button("Bieżący miesiąc", (_, _) => SetMonth(0)),
            Ui.Label("Od:"), _from, Ui.Label("Do:"), _to, Ui.Label("Dział:"), _dept, _search, _onlyAnomalies, _run, _status));
        var bottom = Ui.Toolbar(
            Ui.Button("Eksport zestawienia CSV…", (_, _) => ExportSummary()),
            Ui.Button("Eksport szczegółowy (dni) CSV…", (_, _) => ExportDetails()),
            Ui.Label("Dwuklik – kalendarz pracownika. Godziny w formacie g:mm."));
        bottom.Dock = DockStyle.Bottom;
        Controls.Add(bottom);
        _onlyAnomalies.CheckedChanged += (_, _) => ApplyFilter();
        Load += (_, _) => LoadDepartments();
    }

    public void LoadDepartments()
    {
        _dept.Items.Clear();
        _dept.Items.Add("(wszystkie)");
        foreach (var d in _host.Ctx.Departments.All()) _dept.Items.Add(d);
        _dept.SelectedIndex = 0;
    }

    private void SetMonth(int offset)
    {
        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(offset);
        _from.Value = first;
        _to.Value = first.AddMonths(1).AddDays(-1);
    }

    private async Task Generate()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var from = DateOnly.FromDateTime(_from.Value);
        var to = DateOnly.FromDateTime(_to.Value);
        if (to < from) { Ui.Info("Data końcowa jest wcześniejsza niż początkowa."); return; }
        if (to.DayNumber - from.DayNumber > 366) { Ui.Info("Maksymalny okres zestawienia to 1 rok."); return; }
        var dep = (_dept.SelectedItem as Core.Department)?.Id;
        var text = _search.Text;

        _run.Enabled = false;
        _status.Text = "Liczenie…";
        var progress = new Progress<int>(n => _status.Text = $"Przeliczono: {n:N0}");
        try
        {
            _rows = await Task.Run(() => _host.Reports.Summary(from, to, dep, text, DateTime.Now, progress, ct), ct);
            ApplyFilter();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Ui.ShowError(ex); }
        finally { _run.Enabled = true; }
    }

    private void ApplyFilter()
    {
        var list = _onlyAnomalies.Checked ? _rows.Where(r => r.Summary.AnomalyCount > 0).ToList() : _rows;
        _grid.SetList(list);
        _status.Text = $"Pracowników: {list.Count:N0}   Razem: {WorkTimeCalculator.FormatMinutes(list.Sum(r => r.Summary.WorkedMinutes))} h, " +
                       $"nadgodziny: {WorkTimeCalculator.FormatMinutes(list.Sum(r => r.Summary.OvertimeMinutes))} h";
    }

    private void ExportSummary()
    {
        if (_rows.Count == 0) { Ui.Info("Najpierw wygeneruj zestawienie."); return; }
        if (Ui.SaveFile($"zestawienie-{_from.Value:yyyy-MM-dd}-{_to.Value:yyyy-MM-dd}.csv") is not { } path) return;
        Ui.Try(() =>
        {
            Csv.Write(path,
                new[] { "nr", "nazwisko", "imie", "dzial", "dni_obecnosci", "przepracowano_h", "norma_h", "nadgodziny_h", "niedopracowanie_h" }
                    .Concat(AbsenceTypes.All.Select(AbsenceTypes.Code)).Append("anomalie"),
                _grid.All(), r => new object?[]
                {
                    r.Employee.EmployeeNo, r.Employee.LastName, r.Employee.FirstName, r.Employee.DepartmentName, r.Summary.DaysPresent,
                    Dialogs.FormatHours(r.Summary.WorkedMinutes), Dialogs.FormatHours(r.Summary.NormMinutes),
                    Dialogs.FormatHours(r.Summary.OvertimeMinutes), Dialogs.FormatHours(r.Summary.ShortfallMinutes),
                }.Concat(AbsenceTypes.All.Select(t => (object?)(r.Summary.AbsenceDays.TryGetValue(t, out var n) ? n : 0)))
                 .Append(r.Summary.AnomalyCount));
            Ui.Info($"Zapisano: {path}");
        });
    }

    private void ExportDetails()
    {
        if (_rows.Count == 0) { Ui.Info("Najpierw wygeneruj zestawienie."); return; }
        if (Ui.SaveFile($"ewidencja-dzienna-{_from.Value:yyyy-MM-dd}-{_to.Value:yyyy-MM-dd}.csv") is not { } path) return;
        Ui.Try(() =>
        {
            Csv.Write(path,
                new[] { "nr", "pracownik", "dzial", "data", "wejscie", "wyjscie", "przepracowano_h", "norma_h", "nadgodziny_h", "nieobecnosc", "swieto", "anomalie" },
                _grid.All().SelectMany(r => r.Summary.Days.Select(d => (r.Employee, d))), x => new object?[]
                {
                    x.Employee.EmployeeNo, x.Employee.FullName, x.Employee.DepartmentName, x.d.Date,
                    x.d.FirstIn?.ToString("HH:mm"), x.d.LastOut?.ToString("HH:mm"),
                    Dialogs.FormatHours(x.d.WorkedMinutes), Dialogs.FormatHours(x.d.NormMinutes), Dialogs.FormatHours(x.d.OvertimeMinutes),
                    x.d.Absence is { } a ? AbsenceTypes.Code(a.Type) : null, x.d.DayOffName, string.Join("; ", x.d.Anomalies),
                });
            Ui.Info($"Zapisano: {path}");
        });
    }
}
