using Rcp.App.Controls;
using Rcp.Core;
using Rcp.Core.Data;
using Rcp.Core.Services;

namespace Rcp.App.Forms;

/// <summary>Kartoteka pracowników z wyszukiwaniem i stronicowaniem (tryb wirtualny).</summary>
internal sealed class EmployeesPanel : UserControl
{
    private readonly AppHost _host;
    private readonly VirtualGrid<Employee> _grid = new();
    private readonly TextBox _search = new() { Width = 300, PlaceholderText = "Szukaj: nazwisko, imię, nr, karta, stanowisko…" };
    private readonly ComboBox _dept = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly ComboBox _status = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly Label _count = Ui.Label("");
    private readonly System.Windows.Forms.Timer _debounce;

    public event Action<Employee>? ShowCalendar;

    public EmployeesPanel(AppHost host)
    {
        _host = host;
        _debounce = Ui.Debouncer(300, Reload);
        _status.Items.AddRange(new object[] { "Aktywni", "Nieaktywni", "Wszyscy" });
        _status.SelectedIndex = 0;

        _grid.Column("Nr", e => e.EmployeeNo, 80)
             .Column("Nazwisko", e => e.LastName, 160)
             .Column("Imię", e => e.FirstName, 120)
             .Column("Dział", e => e.DepartmentName, 150)
             .Column("Stanowisko", e => e.Position, 150)
             .Column("Karta", e => e.CardUid, 120)
             .Column("Norma", e => Dialogs.FormatHours(e.DailyNormMinutes), 60)
             .Column("Status", e => e.Active ? "aktywny" : "nieaktywny", 80, fill: true);
        _grid.RowColor = e => e.Active ? null : Ui.MutedColor;
        _grid.ItemActivated += Edit;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Edytuj…", null, (_, _) => { if (_grid.Selected is { } e) Edit(e); });
        menu.Items.Add("Kalendarz / karta ewidencji", null, (_, _) => { if (_grid.Selected is { } e) ShowCalendar?.Invoke(e); });
        menu.Items.Add("Przypisz kartę z czytnika…", null, (_, _) => AssignCard());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Usuń…", null, (_, _) => Delete());
        _grid.Grid.ContextMenuStrip = menu;

        var bottom = Ui.Toolbar(
            Ui.Button("Dodaj…", (_, _) => Add()),
            Ui.Button("Edytuj", (_, _) => { if (_grid.Selected is { } e) Edit(e); }),
            Ui.Button("Przypisz kartę…", (_, _) => AssignCard()),
            Ui.Button("Kalendarz", (_, _) => { if (_grid.Selected is { } e) ShowCalendar?.Invoke(e); }),
            Ui.Button("Usuń", (_, _) => Delete()),
            Ui.Button("Import CSV…", (_, _) => Import()),
            Ui.Button("Eksport CSV…", (_, _) => Export()),
            Ui.Button("Szukaj po karcie…", (_, _) => FindByCard()));
        bottom.Dock = DockStyle.Bottom;

        Controls.Add(_grid.Grid);
        Controls.Add(Ui.Toolbar(Ui.Label("Szukaj:"), _search, Ui.Label("Dział:"), _dept, Ui.Label("Status:"), _status, _count));
        Controls.Add(bottom);

        _search.TextChanged += (_, _) => _debounce.Restart();
        _search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Down) { _grid.Grid.Focus(); e.Handled = true; } };
        _dept.SelectedIndexChanged += (_, _) => Reload();
        _status.SelectedIndexChanged += (_, _) => Reload();
        host.DataChanged += Reload;
        Load += (_, _) => { LoadDepartments(); Reload(); };
    }

    public void LoadDepartments()
    {
        var selected = (_dept.SelectedItem as Department)?.Id;
        _dept.Items.Clear();
        _dept.Items.Add("(wszystkie)");
        foreach (var d in _host.Ctx.Departments.All()) _dept.Items.Add(d);
        _dept.SelectedIndex = 0;
        for (int i = 1; i < _dept.Items.Count; i++)
            if (_dept.Items[i] is Department d && d.Id == selected) _dept.SelectedIndex = i;
    }

    private EmployeeQuery Query => new(
        _search.Text,
        (_dept.SelectedItem as Department)?.Id,
        _status.SelectedIndex switch { 0 => true, 1 => false, _ => null });

    public void Reload()
    {
        var q = Query;
        int total = 0;
        _grid.SetSource(() => total = _host.Ctx.Employees.Count(q), (off, lim) => _host.Ctx.Employees.Search(q, off, lim));
        _count.Text = $"Znaleziono: {total:N0}";
    }

    public void FocusSearch() => _search.Focus();

    private void Add()
    {
        var e = new Employee();
        if (_dept.SelectedItem is Department d) e.DepartmentId = d.Id;
        if (Dialogs.EditEmployee(FindForm()!, _host, e)) _host.NotifyDataChanged();
    }

    private void Edit(Employee e)
    {
        var fresh = _host.Ctx.Employees.Get(e.Id);
        if (fresh != null && Dialogs.EditEmployee(FindForm()!, _host, fresh)) _host.NotifyDataChanged();
    }

    private void AssignCard()
    {
        if (_grid.Selected is not { } e) return;
        if (Dialogs.CaptureCard(FindForm()!, _host, $"Karta dla: {e.FullName}") is not { } uid) return;
        if (!string.IsNullOrEmpty(e.CardUid) && e.CardUid != uid && !Ui.Confirm($"Zastąpić dotychczasową kartę {e.CardUid} kartą {uid}?")) return;
        Ui.Try(() =>
        {
            e.CardUid = uid;
            _host.Ctx.Employees.Save(e);
            int n = _host.Ctx.Events.AssignUnknownCard(uid, e.Id);
            _host.Ctx.Audit.Log("Przypisanie karty", $"{e.EmployeeNo} karta={uid}");
            _host.NotifyDataChanged();
            Ui.Info($"Przypisano kartę {uid}: {e.FullName}" + (n > 0 ? $"\nPrzypisano też {n} wcześniejszych odbić." : ""));
        });
    }

    private void FindByCard()
    {
        if (Dialogs.CaptureCard(FindForm()!, _host, "Kto ma tę kartę?") is not { } uid) return;
        var e = _host.Ctx.Employees.GetByCard(uid);
        if (e == null) { Ui.Info($"Karta {uid} nie jest przypisana do żadnego pracownika."); return; }
        _status.SelectedIndex = 2;
        _search.Text = uid;
        _debounce.Stop();
        Reload();
    }

    private void Delete()
    {
        if (_grid.Selected is not { } e) return;
        var choice = MessageBox.Show(
            $"Pracownik: {e.FullName} ({e.EmployeeNo})\n\n" +
            "TAK – oznacz jako nieaktywnego (zalecane, historia zostaje)\n" +
            "NIE – usuń trwale wraz z całą historią odbić i nieobecności",
            "Usuwanie pracownika", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (choice == DialogResult.Cancel) return;
        Ui.Try(() =>
        {
            if (choice == DialogResult.Yes)
            {
                e.Active = false;
                e.CardUid = null;
                _host.Ctx.Employees.Save(e);
                _host.Ctx.Audit.Log("Dezaktywacja pracownika", $"{e.EmployeeNo} {e.FullName}");
            }
            else if (Ui.Confirm("Na pewno usunąć trwale? Tej operacji nie można cofnąć."))
            {
                _host.Ctx.Employees.Delete(e.Id);
                _host.Ctx.Audit.Log("Usunięcie pracownika", $"{e.EmployeeNo} {e.FullName}");
            }
            _host.NotifyDataChanged();
        });
    }

    private void Import()
    {
        using var d = new OpenFileDialog { Filter = "Pliki CSV (*.csv;*.txt)|*.csv;*.txt" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        Ui.Try(() =>
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                var (ins, upd, errors) = _host.Ctx.Employees.Upsert(EmployeeCsv.Parse(d.FileName));
                _host.Ctx.Audit.Log("Import pracowników", $"{d.FileName}: +{ins}, ~{upd}, błędy {errors.Count}");
                LoadDepartments();
                _host.NotifyDataChanged();
                var msg = $"Dodano: {ins:N0}\nZaktualizowano: {upd:N0}";
                if (errors.Count > 0) msg += $"\n\nBłędy ({errors.Count}):\n" + string.Join("\n", errors.Take(20)) + (errors.Count > 20 ? "\n…" : "");
                Ui.Info(msg);
            }
            finally { Cursor = Cursors.Default; }
        });
    }

    private void Export()
    {
        if (Ui.SaveFile($"pracownicy-{DateTime.Today:yyyy-MM-dd}.csv") is not { } path) return;
        Ui.Try(() =>
        {
            Csv.Write(path, EmployeeCsv.Headers, _host.Ctx.Employees.StreamAll(Query), e => new object?[]
            {
                e.EmployeeNo, e.LastName, e.FirstName, e.DepartmentName, e.Position, e.CardUid,
                Dialogs.FormatHours(e.DailyNormMinutes), e.Active ? "1" : "0",
            });
            Ui.Info($"Zapisano: {path}");
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _host.DataChanged -= Reload;
            _debounce.Dispose();
        }
        base.Dispose(disposing);
    }
}
