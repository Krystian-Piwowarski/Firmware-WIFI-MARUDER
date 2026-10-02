using Rcp.App.Controls;
using Rcp.Core;
using Rcp.Core.Data;
using Rcp.Core.Services;

namespace Rcp.App.Forms;

/// <summary>Rejestr wszystkich zdarzeń wejścia/wyjścia z filtrowaniem.</summary>
internal sealed class EventsPanel : UserControl
{
    private readonly AppHost _host;
    private readonly VirtualGrid<AttendanceEvent> _grid = new();
    private readonly DateTimePicker _from = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly DateTimePicker _to = new() { Format = DateTimePickerFormat.Short, Width = 110 };
    private readonly TextBox _search = new() { Width = 220, PlaceholderText = "Pracownik, nr, karta…" };
    private readonly ComboBox _reader = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly CheckBox _unknown = new() { Text = "Tylko nieznane karty", AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
    private readonly Label _count = Ui.Label("");
    private readonly System.Windows.Forms.Timer _debounce;

    public event Action<Employee>? ShowEmployee;

    public EventsPanel(AppHost host)
    {
        _host = host;
        _debounce = Ui.Debouncer(300, Reload);
        _from.Value = DateTime.Today.AddDays(-7);
        _to.Value = DateTime.Today;

        _grid.Column("Data i czas", e => e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"), 140)
             .Column("Kierunek", e => Ui.Dir(e.Direction), 75)
             .Column("Pracownik", e => e.EmployeeName ?? "NIEZNANA KARTA", 200)
             .Column("Nr", e => e.EmployeeNo, 70)
             .Column("Dział", e => e.DepartmentName, 130)
             .Column("Karta", e => e.CardUid, 110)
             .Column("Czytnik", e => e.ReaderName, 130)
             .Column("Źródło", e => e.Source == EventSource.Manual ? "ręcznie" : "czytnik", 70)
             .Column("Uwagi", e => e.Note, 150, fill: true);
        _grid.RowColor = e => e.EmployeeId == null ? Ui.ErrorColor : e.Source == EventSource.Manual ? Ui.WarnColor : null;
        _grid.ItemActivated += e => { if (e.EmployeeId != null) Edit(e); };

        var bottom = Ui.Toolbar(
            Ui.Button("Dodaj ręcznie…", (_, _) => Add()),
            Ui.Button("Koryguj…", (_, _) => { if (_grid.Selected is { EmployeeId: not null } e) Edit(e); }),
            Ui.Button("Usuń", (_, _) => Delete()),
            Ui.Button("Przypisz kartę pracownikowi…", (_, _) => AssignCard()),
            Ui.Button("Kalendarz pracownika", (_, _) => OpenEmployee()),
            Ui.Button("Eksport CSV…", (_, _) => Export()));
        bottom.Dock = DockStyle.Bottom;

        Controls.Add(_grid.Grid);
        Controls.Add(Ui.Toolbar(
            Ui.Label("Od:"), _from, Ui.Label("Do:"), _to, Ui.Label("Szukaj:"), _search,
            Ui.Label("Czytnik:"), _reader, _unknown, Ui.Button("Odśwież", (_, _) => Reload()), _count));
        Controls.Add(bottom);

        _search.TextChanged += (_, _) => _debounce.Restart();
        _from.ValueChanged += (_, _) => _debounce.Restart();
        _to.ValueChanged += (_, _) => _debounce.Restart();
        _reader.SelectedIndexChanged += (_, _) => Reload();
        _unknown.CheckedChanged += (_, _) => Reload();
        host.DataChanged += Reload;
        host.Scanned += OnScanned;
        Load += (_, _) => { LoadReaders(); Reload(); };
    }

    public void LoadReaders()
    {
        _reader.Items.Clear();
        _reader.Items.Add("(wszystkie)");
        foreach (var r in _host.Ctx.Readers.All()) _reader.Items.Add(r);
        _reader.SelectedIndex = 0;
    }

    private EventQuery Query => new(
        _from.Value.Date,
        _to.Value.Date.AddDays(1),
        _search.Text,
        null,
        (_reader.SelectedItem as ReaderConfig)?.Id,
        _unknown.Checked);

    public void Reload()
    {
        if (!IsHandleCreated) return;
        var q = Query;
        int total = 0;
        _grid.SetSource(() => total = _host.Ctx.Events.Count(q), (off, lim) => _host.Ctx.Events.Search(q, off, lim));
        _count.Text = $"Zdarzeń: {total:N0}";
    }

    private void OnScanned(ScanResult r)
    {
        if (Visible && r.Status != ScanStatus.Ignored) _debounce.Restart();
    }

    private void Add()
    {
        var evt = new AttendanceEvent { Timestamp = DateTime.Now, Direction = Direction.In };
        if (Dialogs.EditEvent(FindForm()!, _host.Ctx, evt)) _host.NotifyDataChanged();
    }

    private void Edit(AttendanceEvent e)
    {
        if (Dialogs.EditEvent(FindForm()!, _host.Ctx, e)) _host.NotifyDataChanged();
    }

    private void Delete()
    {
        if (_grid.Selected is not { } e) return;
        if (!Ui.Confirm($"Usunąć zdarzenie {e.Timestamp:dd.MM.yyyy HH:mm:ss} ({e.EmployeeName ?? e.CardUid})?")) return;
        _host.Ctx.Events.Delete(e.Id);
        _host.Ctx.Audit.Log("Usunięcie zdarzenia", $"{e.EmployeeNo} {e.Timestamp:yyyy-MM-dd HH:mm:ss} {e.Direction} karta={e.CardUid}");
        _host.NotifyDataChanged();
    }

    private void AssignCard()
    {
        if (_grid.Selected is not { EmployeeId: null } e) { Ui.Info("Zaznacz odbicie nieznanej karty (czerwony wiersz)."); return; }
        if (Dialogs.PickEmployee(FindForm()!, _host.Ctx, $"Komu przypisać kartę {e.CardUid}?") is not { } emp) return;
        Ui.Try(() =>
        {
            emp.CardUid = e.CardUid;
            _host.Ctx.Employees.Save(emp);
            int n = _host.Ctx.Events.AssignUnknownCard(e.CardUid, emp.Id);
            _host.Ctx.Audit.Log("Przypisanie karty", $"{emp.EmployeeNo} karta={e.CardUid}, odbić: {n}");
            _host.NotifyDataChanged();
            Ui.Info($"Karta {e.CardUid} przypisana: {emp.FullName}. Zaktualizowano odbić: {n}.");
        });
    }

    private void OpenEmployee()
    {
        if (_grid.Selected is { EmployeeId: { } id } && _host.Ctx.Employees.Get(id) is { } emp) ShowEmployee?.Invoke(emp);
    }

    private void Export()
    {
        if (Ui.SaveFile($"zdarzenia-{_from.Value:yyyy-MM-dd}-{_to.Value:yyyy-MM-dd}.csv") is not { } path) return;
        Ui.Try(() =>
        {
            Csv.Write(path, new[] { "data_czas", "kierunek", "nr", "pracownik", "dzial", "karta", "czytnik", "zrodlo", "uwagi" },
                _host.Ctx.Events.StreamAll(Query), e => new object?[]
                {
                    e.Timestamp, e.Direction == Direction.In ? "WE" : "WY", e.EmployeeNo, e.EmployeeName, e.DepartmentName,
                    e.CardUid, e.ReaderName, e.Source == EventSource.Manual ? "reczne" : "czytnik", e.Note,
                });
            Ui.Info($"Zapisano: {path}");
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _host.DataChanged -= Reload;
            _host.Scanned -= OnScanned;
            _debounce.Dispose();
        }
        base.Dispose(disposing);
    }
}
