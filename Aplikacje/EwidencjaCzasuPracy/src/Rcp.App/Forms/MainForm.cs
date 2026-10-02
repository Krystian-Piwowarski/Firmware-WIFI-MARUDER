using System.Diagnostics;
using Rcp.Core;
using Rcp.Core.Services;

namespace Rcp.App.Forms;

internal sealed class MainForm : Form
{
    private readonly AppHost _host;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
    private readonly MonitorPanel _monitor;
    private readonly EmployeesPanel _employees;
    private readonly CalendarPanel _calendar;
    private readonly EventsPanel _events;
    private readonly ReportsPanel _reports;
    private readonly TabPage _calendarTab;
    private readonly ToolStripStatusLabel _statusDb = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _statusReaders = new();
    private readonly ToolStripStatusLabel _statusClock = new();
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 1000 };

    public MainForm(RcpContext ctx, AppConfig config)
    {
        _host = new AppHost(ctx, config);
        Ui.EnableDpiScaling(this);
        Text = "Ewidencja Czasu Pracy RCP" + (ctx.Settings.CompanyName is { Length: > 0 } c ? $" – {c}" : "");
        Size = new Size(1280, 820);
        MinimumSize = new Size(1000, 640);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        Font = new Font("Segoe UI", 9f);
        Icon = SystemIcons.Application;

        _monitor = new MonitorPanel(_host) { Dock = DockStyle.Fill };
        _employees = new EmployeesPanel(_host) { Dock = DockStyle.Fill };
        _calendar = new CalendarPanel(_host) { Dock = DockStyle.Fill };
        _events = new EventsPanel(_host) { Dock = DockStyle.Fill };
        _reports = new ReportsPanel(_host) { Dock = DockStyle.Fill };

        _tabs.TabPages.Add(Page("Monitor (na żywo)", _monitor));
        _tabs.TabPages.Add(Page("Pracownicy", _employees));
        _tabs.TabPages.Add(_calendarTab = Page("Kalendarz", _calendar));
        _tabs.TabPages.Add(Page("Zdarzenia", _events));
        _tabs.TabPages.Add(Page("Zestawienia", _reports));

        _monitor.ShowEmployee += ShowCalendar;
        _employees.ShowCalendar += ShowCalendar;
        _events.ShowEmployee += ShowCalendar;
        _reports.ShowEmployee += ShowCalendar;

        var status = new StatusStrip();
        status.Items.AddRange(new ToolStripItem[] { _statusDb, _statusReaders, _statusClock });

        var menu = BuildMenu();
        Controls.Add(_tabs);
        Controls.Add(menu);
        Controls.Add(status);
        MainMenuStrip = menu;

        _host.Readers.StatusChanged += UpdateStatus;
        _host.DataChanged += UpdateStatus;
        _clock.Tick += (_, _) => _statusClock.Text = DateTime.Now.ToString("dddd, dd.MM.yyyy  HH:mm:ss");
        _clock.Start();
        KeyPreview = true;
        KeyDown += OnShortcut;

        Load += (_, _) =>
        {
            Ui.ScaleColumns(this);
            Ui.Try(_host.ReloadReaders);
            UpdateStatus();
            if (ctx.Readers.All().Count == 0)
                BeginInvoke(() =>
                {
                    if (Ui.Confirm("Nie skonfigurowano jeszcze żadnego czytnika kart.\nCzy chcesz to zrobić teraz?")) ShowReaders();
                });
        };
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing &&
                !Ui.Confirm("Zamknięcie programu zatrzyma rejestrację odbić kart.\nCzy na pewno zamknąć?"))
                e.Cancel = true;
        };
        FormClosed += (_, _) => { _clock.Dispose(); _host.Dispose(); };
    }

    private static TabPage Page(string title, Control content)
    {
        var p = new TabPage(title);
        p.Controls.Add(content);
        return p;
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("&Plik");
        file.DropDownItems.Add("Kopia zapasowa bazy…", null, (_, _) => Backup());
        file.DropDownItems.Add("Otwórz folder danych", null, (_, _) => OpenFolder(Path.GetDirectoryName(Path.GetFullPath(_host.Config.DatabasePath))!));
        file.DropDownItems.Add("Otwórz folder dzienników", null, (_, _) => OpenFolder(Log.Directory));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Zakończ", null, (_, _) => Close());

        var cfg = new ToolStripMenuItem("&Konfiguracja");
        cfg.DropDownItems.Add("Czytniki kart…", null, (_, _) => ShowReaders());
        cfg.DropDownItems.Add("Działy…", null, (_, _) =>
        {
            using var f = new DepartmentsForm(_host.Ctx);
            f.ShowDialog(this);
            _employees.LoadDepartments();
            _reports.LoadDepartments();
            _host.NotifyDataChanged();
        });
        cfg.DropDownItems.Add("Dni wolne i święta…", null, (_, _) =>
        {
            using var f = new DaysOffForm(_host.Ctx);
            f.ShowDialog(this);
            _host.NotifyDataChanged();
        });
        cfg.DropDownItems.Add("Ustawienia…", null, (_, _) =>
        {
            if (Dialogs.EditSettings(this, _host))
                Text = "Ewidencja Czasu Pracy RCP" + (_host.Ctx.Settings.CompanyName is { Length: > 0 } c ? $" – {c}" : "");
        });

        var help = new ToolStripMenuItem("Pomo&c");
        help.DropDownItems.Add("Skróty klawiszowe", null, (_, _) => Ui.Info(
            "F2 – monitor\nF3 – szukaj pracownika\nF4 – kalendarz\nF5 – zdarzenia\nF6 – zestawienia\nCtrl+N – nowy pracownik"));
        help.DropDownItems.Add("O programie", null, (_, _) => Ui.Info(
            $"Ewidencja Czasu Pracy RCP {Application.ProductVersion}\n\n" +
            "Rejestracja wejść i wyjść kartami Mifare, kalendarz, nieobecności, zestawienia.\n" +
            $"Baza danych: {_host.Config.DatabasePath}"));

        menu.Items.AddRange(new ToolStripItem[] { file, cfg, help });
        return menu;
    }

    private void OnShortcut(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.F2: _tabs.SelectedIndex = 0; break;
            case Keys.F3: _tabs.SelectedIndex = 1; _employees.FocusSearch(); break;
            case Keys.F4: _tabs.SelectedIndex = 2; break;
            case Keys.F5: _tabs.SelectedIndex = 3; _events.Reload(); break;
            case Keys.F6: _tabs.SelectedIndex = 4; break;
            case Keys.N when e.Control:
                if (Dialogs.EditEmployee(this, _host, new Employee())) _host.NotifyDataChanged();
                break;
            default: return;
        }
        e.Handled = true;
    }

    private void ShowCalendar(Employee e)
    {
        _tabs.SelectedTab = _calendarTab;
        _calendar.ShowEmployee(e);
    }

    private void ShowReaders()
    {
        using var f = new ReadersForm(_host);
        f.ShowDialog(this);
        _events.LoadReaders();
    }

    private void UpdateStatus()
    {
        var drivers = _host.Readers.Drivers;
        int ok = drivers.Count(d => d.IsOk);
        _statusReaders.Text = drivers.Count == 0 ? "Czytniki: brak" : $"Czytniki: {ok}/{drivers.Count} OK";
        _statusReaders.ForeColor = ok == drivers.Count && drivers.Count > 0 ? Color.DarkGreen : Color.Firebrick;
        try
        {
            _statusDb.Text = $"Baza: {_host.Config.DatabasePath}   •   Pracowników: {_host.Ctx.Employees.TotalCount():N0}";
        }
        catch (Exception ex)
        {
            _statusDb.Text = "Błąd bazy: " + ex.Message;
        }
    }

    private void Backup()
    {
        if (Ui.SaveFile($"rcp-kopia-{DateTime.Now:yyyy-MM-dd-HHmm}.db", "Baza SQLite (*.db)|*.db") is not { } path) return;
        Ui.Try(() =>
        {
            Cursor = Cursors.WaitCursor;
            try { _host.Ctx.Db.Backup(path); }
            finally { Cursor = Cursors.Default; }
            _host.Ctx.Audit.Log("Kopia zapasowa", path);
            Ui.Info($"Kopia zapisana:\n{path}");
        });
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
