using System.IO.Ports;
using Rcp.App.Readers;
using Rcp.Core;
using Rcp.Core.Data;
using Rcp.Core.Services;

namespace Rcp.App.Forms;

/// <summary>Lista czytników z ich bieżącym stanem.</summary>
internal sealed class ReadersForm : Form
{
    private readonly AppHost _host;
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
    };

    public ReadersForm(AppHost host)
    {
        _host = host;
        Ui.EnableDpiScaling(this);
        Text = "Czytniki kart";
        Size = new Size(900, 420);
        StartPosition = FormStartPosition.CenterParent;
        Font = SystemFonts.MessageBoxFont;
        _list.Columns.Add("Nazwa", 180);
        _list.Columns.Add("Typ", 120);
        _list.Columns.Add("Kierunek", 110);
        _list.Columns.Add("Urządzenie", 260);
        _list.Columns.Add("Stan", 220);
        _list.DoubleClick += (_, _) => Edit();

        Controls.Add(_list);
        Controls.Add(Ui.Toolbar(
            Ui.Button("Dodaj…", (_, _) => Add()),
            Ui.Button("Edytuj…", (_, _) => Edit()),
            Ui.Button("Usuń", (_, _) => Delete()),
            Ui.Label("Zmiany są stosowane od razu – czytniki zostaną ponownie uruchomione.")));
        host.Readers.StatusChanged += RefreshList;
        Load += (_, _) => Ui.ScaleColumns(this);
        FormClosed += (_, _) => host.Readers.StatusChanged -= RefreshList;
        RefreshList();
    }

    private void RefreshList()
    {
        var status = _host.Readers.Drivers.ToDictionary(d => d.Config.Id);
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var r in _host.Ctx.Readers.All())
        {
            var st = status.TryGetValue(r.Id, out var d) ? d.Status : r.Enabled ? "—" : "Wyłączony";
            var item = new ListViewItem(new[] { r.Name, KindName(r.Kind), ModeName(r.Mode), Short(r.DeviceId), st }) { Tag = r };
            if (d != null) item.BackColor = d.IsOk ? Ui.InColor : Ui.ErrorColor;
            _list.Items.Add(item);
        }
        _list.EndUpdate();
    }

    private static string Short(string s) => s.Length > 60 ? "…" + s[^57..] : s;

    private ReaderConfig? Selected => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as ReaderConfig : null;

    private void Add()
    {
        var r = new ReaderConfig { Name = $"Czytnik {_list.Items.Count + 1}" };
        if (ReaderEditor.Show(this, _host, r)) Apply();
    }

    private void Edit()
    {
        if (Selected is { } r && ReaderEditor.Show(this, _host, r)) Apply();
    }

    private void Delete()
    {
        if (Selected is not { } r || !Ui.Confirm($"Usunąć czytnik „{r.Name}”? Zarejestrowane zdarzenia pozostaną.")) return;
        _host.Ctx.Readers.Delete(r.Id);
        Apply();
    }

    private void Apply()
    {
        _host.ReloadReaders();
        RefreshList();
    }

    public static string KindName(ReaderKind k) => k switch
    {
        ReaderKind.Keyboard => "USB (klawiatura)",
        ReaderKind.Serial => "Port COM",
        ReaderKind.Tcp => "Sieć TCP/IP",
        ReaderKind.PcSc => "PC/SC",
        _ => k.ToString(),
    };

    public static string ModeName(ReaderMode m) => m switch
    {
        ReaderMode.In => "Wejście",
        ReaderMode.Out => "Wyjście",
        _ => "Wejście/wyjście (naprzemiennie)",
    };
}

internal static class ReaderEditor
{
    public static bool Show(IWin32Window owner, AppHost host, ReaderConfig r)
    {
        var name = new TextBox { Text = r.Name };
        var kind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var k in Enum.GetValues<ReaderKind>()) kind.Items.Add(new KeyValuePair<ReaderKind, string>(k, ReadersForm.KindName(k)));
        kind.DisplayMember = "Value";
        var mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var m in Enum.GetValues<ReaderMode>()) mode.Items.Add(new KeyValuePair<ReaderMode, string>(m, ReadersForm.ModeName(m)));
        mode.DisplayMember = "Value";
        mode.SelectedIndex = Array.IndexOf(Enum.GetValues<ReaderMode>(), r.Mode);

        var device = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 380, Text = r.DeviceId };
        var hint = new Label { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(420, 0) };
        var detect = Ui.Button("Wykryj…", (_, _) => { });
        var deviceRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        deviceRow.Controls.AddRange(new Control[] { device, detect });

        var baud = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Text = r.BaudRate.ToString() };
        baud.Items.AddRange(new object[] { "2400", "4800", "9600", "19200", "38400", "57600", "115200" });
        var dec = new CheckBox { Text = "Czytnik podaje numer dziesiętny – zamień na HEX", Checked = r.DecimalToHex, AutoSize = true };
        var rev = new CheckBox { Text = "Odwróć kolejność bajtów UID", Checked = r.ReverseBytes, AutoSize = true };
        var enabled = new CheckBox { Text = "Czytnik włączony", Checked = r.Enabled, AutoSize = true };

        void KindChanged()
        {
            var k = ((KeyValuePair<ReaderKind, string>)kind.SelectedItem!).Key;
            device.Items.Clear();
            baud.Enabled = k == ReaderKind.Serial;
            detect.Visible = k is ReaderKind.Keyboard or ReaderKind.PcSc or ReaderKind.Serial;
            switch (k)
            {
                case ReaderKind.Keyboard:
                    hint.Text = "Kliknij „Wykryj…” i przyłóż kartę do TEGO czytnika – program zapamięta jego identyfikator USB. "
                                + "Dzięki temu czytnik wejścia i wyjścia są rozróżniane, nawet gdy oba działają jak klawiatura.";
                    detect.Text = "Wykryj…";
                    break;
                case ReaderKind.Serial:
                    device.Items.AddRange(SerialPort.GetPortNames().OrderBy(p => p.Length).ThenBy(p => p).Cast<object>().ToArray());
                    hint.Text = "Port COM czytnika (RS232, RS485 przez konwerter lub USB-COM). Numer karty jest odczytywany jako tekst zakończony Enter/ETX.";
                    detect.Text = "Odśwież";
                    break;
                case ReaderKind.Tcp:
                    hint.Text = "Adres czytnika sieciowego w formacie IP:port, np. 192.168.1.50:4001. Program łączy się jako klient TCP.";
                    break;
                case ReaderKind.PcSc:
                    device.Items.AddRange(PcscReaderDriver.ListReaders().Cast<object>().ToArray());
                    hint.Text = "Czytnik PC/SC (np. ACR122U). Pozostaw puste, aby użyć pierwszego wykrytego czytnika.";
                    detect.Text = "Odśwież";
                    break;
            }
        }

        detect.Click += (_, _) =>
        {
            var k = ((KeyValuePair<ReaderKind, string>)kind.SelectedItem!).Key;
            if (k == ReaderKind.Keyboard)
            {
                if (DetectKeyboardDevice(owner, host) is { } path) device.Text = path;
            }
            else KindChanged();
        };
        kind.SelectedIndexChanged += (_, _) => KindChanged();
        kind.SelectedIndex = Array.IndexOf(Enum.GetValues<ReaderKind>(), r.Kind);

        var grid = Ui.FormGrid(("Nazwa:", name), ("Typ podłączenia:", kind), ("Kierunek:", mode), ("Urządzenie:", deviceRow),
            ("", hint), ("Prędkość [bps]:", baud), ("", dec), ("", rev), ("", enabled));

        using var form = Ui.Dialog(r.Id == 0 ? "Nowy czytnik" : $"Czytnik: {r.Name}", grid, () =>
        {
            var k = ((KeyValuePair<ReaderKind, string>)kind.SelectedItem!).Key;
            if (k == ReaderKind.Tcp && TcpReaderDriver.ParseEndpoint(device.Text).Host == null)
                throw new RcpException("Podaj adres czytnika w formacie host:port.");
            if (k == ReaderKind.Serial && string.IsNullOrWhiteSpace(device.Text))
                throw new RcpException("Wybierz port COM.");
            if (k == ReaderKind.Keyboard && string.IsNullOrWhiteSpace(device.Text))
                throw new RcpException("Użyj przycisku „Wykryj…”, aby wskazać czytnik USB.");
            r.Name = name.Text.Trim();
            r.Kind = k;
            r.Mode = ((KeyValuePair<ReaderMode, string>)mode.SelectedItem!).Key;
            r.DeviceId = device.Text.Trim();
            r.BaudRate = int.TryParse(baud.Text, out var b) ? b : 9600;
            r.DecimalToHex = dec.Checked;
            r.ReverseBytes = rev.Checked;
            r.Enabled = enabled.Checked;
            host.Ctx.Readers.Save(r);
            host.Ctx.Audit.Log("Konfiguracja czytnika", $"{r.Name} {r.Kind} {r.Mode} {r.DeviceId}");
            return true;
        }, 620);
        return form.ShowDialog(owner) == DialogResult.OK;
    }

    private static string? DetectKeyboardDevice(IWin32Window owner, AppHost host)
    {
        var label = new Label
        {
            Text = "Przyłóż kartę do czytnika, który chcesz skonfigurować…",
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 11f),
            Padding = new Padding(10),
        };
        string? found = null;
        using var form = Ui.Dialog("Wykrywanie czytnika USB", label, () => found != null, 480);
        void OnScan(string dev, string text)
        {
            found = dev;
            form.DialogResult = DialogResult.OK;
        }
        RawInputKeyboardHub hub;
        try { hub = host.Readers.KeyboardHub; }
        catch (Exception ex) { Ui.ShowError(ex); return null; }
        hub.Scan += OnScan;
        hub.CaptureMode = true;
        host.CardCapture = _ => { }; // podczas wykrywania nie rejestruj odbić
        try { return form.ShowDialog(owner) == DialogResult.OK ? found : null; }
        finally
        {
            hub.Scan -= OnScan;
            hub.CaptureMode = false;
            host.CardCapture = null;
        }
    }
}

/// <summary>Słownik działów.</summary>
internal sealed class DepartmentsForm : Form
{
    private readonly RcpContext _ctx;
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };

    public DepartmentsForm(RcpContext ctx)
    {
        _ctx = ctx;
        Ui.EnableDpiScaling(this);
        Text = "Działy";
        Size = new Size(460, 480);
        StartPosition = FormStartPosition.CenterParent;
        Font = SystemFonts.MessageBoxFont;
        Controls.Add(_list);
        Controls.Add(Ui.Toolbar(
            Ui.Button("Dodaj…", (_, _) => Edit(new Department())),
            Ui.Button("Zmień nazwę…", (_, _) => { if (_list.SelectedItem is Department d) Edit(d); }),
            Ui.Button("Usuń", (_, _) => Delete())));
        _list.DoubleClick += (_, _) => { if (_list.SelectedItem is Department d) Edit(d); };
        Reload();
    }

    private void Reload()
    {
        _list.Items.Clear();
        foreach (var d in _ctx.Departments.All()) _list.Items.Add(d);
    }

    private void Edit(Department d)
    {
        var name = new TextBox { Text = d.Name };
        using var form = Ui.Dialog(d.Id == 0 ? "Nowy dział" : "Zmiana nazwy działu", Ui.FormGrid(("Nazwa:", name)), () =>
        {
            _ctx.Departments.Save(new Department { Id = d.Id, Name = name.Text });
            return true;
        });
        if (form.ShowDialog(this) == DialogResult.OK) Reload();
    }

    private void Delete()
    {
        if (_list.SelectedItem is not Department d) return;
        if (!Ui.Confirm($"Usunąć dział „{d.Name}”? Pracownicy pozostaną bez przypisanego działu.")) return;
        _ctx.Departments.Delete(d.Id);
        Reload();
    }
}

/// <summary>Dni wolne: święta ustawowe (podgląd) i dodatkowe dni wolne firmy.</summary>
internal sealed class DaysOffForm : Form
{
    private readonly RcpContext _ctx;
    private readonly ListView _company = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
    private readonly ListView _statutory = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
    private readonly NumericUpDown _year = new() { Minimum = 2000, Maximum = 2100, Value = DateTime.Today.Year, Width = 80 };

    public DaysOffForm(RcpContext ctx)
    {
        _ctx = ctx;
        Ui.EnableDpiScaling(this);
        Text = "Dni wolne od pracy";
        Size = new Size(760, 520);
        StartPosition = FormStartPosition.CenterParent;
        Font = SystemFonts.MessageBoxFont;

        _company.Columns.Add("Data", 110);
        _company.Columns.Add("Opis", 220);
        _statutory.Columns.Add("Data", 110);
        _statutory.Columns.Add("Święto", 220);

        var split = new SplitContainer { Dock = DockStyle.Fill };
        var left = new GroupBox { Text = "Dodatkowe dni wolne w firmie", Dock = DockStyle.Fill };
        left.Controls.Add(_company);
        left.Controls.Add(Ui.Toolbar(Ui.Button("Dodaj…", (_, _) => Add()), Ui.Button("Usuń", (_, _) => Delete())));
        var right = new GroupBox { Text = "Święta ustawowe (wyliczane automatycznie)", Dock = DockStyle.Fill };
        right.Controls.Add(_statutory);
        right.Controls.Add(Ui.Toolbar(Ui.Label("Rok:"), _year));
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        Controls.Add(split);
        _year.ValueChanged += (_, _) => Reload();
        Load += (_, _) =>
        {
            Ui.ScaleColumns(this);
            try { split.SplitterDistance = split.Width / 2; } catch (Exception) { }
        };
        Reload();
    }

    private void Reload()
    {
        _company.Items.Clear();
        foreach (var d in _ctx.Calendar.All())
            _company.Items.Add(new ListViewItem(new[] { d.Date.ToString("yyyy-MM-dd ddd"), d.Name }) { Tag = d });
        _statutory.Items.Clear();
        foreach (var (date, name) in PolishHolidays.ForYear((int)_year.Value).OrderBy(x => x.Key))
            _statutory.Items.Add(new ListViewItem(new[] { date.ToString("yyyy-MM-dd ddd"), name }));
    }

    private void Add()
    {
        var date = new DateTimePicker { Format = DateTimePickerFormat.Short };
        var name = new TextBox { Text = "Dzień wolny" };
        using var form = Ui.Dialog("Dzień wolny", Ui.FormGrid(("Data:", date), ("Opis:", name)), () =>
        {
            _ctx.Calendar.Save(new CompanyDayOff { Date = DateOnly.FromDateTime(date.Value), Name = name.Text.Trim() });
            return true;
        });
        if (form.ShowDialog(this) == DialogResult.OK) Reload();
    }

    private void Delete()
    {
        if (_company.SelectedItems.Count == 0 || _company.SelectedItems[0].Tag is not CompanyDayOff d) return;
        _ctx.Calendar.Delete(d.Date);
        Reload();
    }
}
