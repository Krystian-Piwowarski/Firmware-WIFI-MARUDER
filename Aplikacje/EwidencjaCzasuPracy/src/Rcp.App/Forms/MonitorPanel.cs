using Rcp.Core;
using Rcp.Core.Services;

namespace Rcp.App.Forms;

/// <summary>Podgląd na żywo: ostatnie odbicia, obecni w pracy, stan czytników.</summary>
internal sealed class MonitorPanel : UserControl
{
    private const int MaxRecent = 300;
    private readonly AppHost _host;
    private readonly Label _big = new()
    {
        Dock = DockStyle.Top, Height = 70, TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 22f, FontStyle.Bold), Text = "Oczekiwanie na odbicie karty…", BackColor = Ui.MutedColor,
    };
    private readonly Label _sub = new()
    {
        Dock = DockStyle.Top, Height = 26, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 10f), BackColor = Ui.MutedColor,
    };
    private readonly ListView _recent = NewList();
    private readonly ListView _present = NewList();
    private readonly GroupBox _presentBox = new() { Text = "Obecni w pracy", Dock = DockStyle.Fill };
    private readonly TextBox _presentFilter = new() { Dock = DockStyle.Top, PlaceholderText = "Filtruj obecnych…" };
    private readonly FlowLayoutPanel _readers = new() { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(4), BackColor = Ui.MutedColor };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 60_000 };
    private readonly System.Windows.Forms.Timer _clearBig = new() { Interval = 8_000 };
    private readonly System.Windows.Forms.Timer _presentDebounce;
    private List<AttendanceEvent> _presentAll = new();

    public event Action<Employee>? ShowEmployee;

    public MonitorPanel(AppHost host)
    {
        _host = host;
        // przy wielu odbiciach naraz (zmiana zmian) lista obecnych odświeża się najwyżej co 2 s
        _presentDebounce = Ui.Debouncer(2_000, RefreshPresent);
        _recent.Columns.Add("Czas", 80);
        _recent.Columns.Add("Kierunek", 75);
        _recent.Columns.Add("Pracownik", 220);
        _recent.Columns.Add("Nr", 70);
        _recent.Columns.Add("Karta", 110);
        _recent.Columns.Add("Czytnik", 130);
        _present.Columns.Add("Pracownik", 220);
        _present.Columns.Add("Dział", 130);
        _present.Columns.Add("Od", 110);

        var split = new SplitContainer { Dock = DockStyle.Fill };
        var recentBox = new GroupBox { Text = "Ostatnie odbicia", Dock = DockStyle.Fill };
        recentBox.Controls.Add(_recent);
        _presentBox.Controls.Add(_present);
        _presentBox.Controls.Add(_presentFilter);
        split.Panel1.Controls.Add(recentBox);
        split.Panel2.Controls.Add(_presentBox);

        Controls.Add(split);
        Controls.Add(_readers);
        Controls.Add(_sub);
        Controls.Add(_big);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Przypisz tę kartę pracownikowi…", null, (_, _) => AssignCard());
        menu.Items.Add("Pokaż kalendarz pracownika", null, (_, _) => OpenEmployee(_recent));
        _recent.ContextMenuStrip = menu;
        _recent.DoubleClick += (_, _) => OpenEmployee(_recent);
        _present.DoubleClick += (_, _) => OpenEmployee(_present);
        _presentFilter.TextChanged += (_, _) => FillPresent();

        host.Scanned += OnScanned;
        host.DataChanged += RefreshPresent;
        host.Readers.StatusChanged += RefreshReaders;
        _refresh.Tick += (_, _) => RefreshPresent();
        _clearBig.Tick += (_, _) => { _clearBig.Stop(); _big.BackColor = _sub.BackColor = Ui.MutedColor; };
        _refresh.Start();

        Load += (_, _) =>
        {
            try { split.SplitterDistance = split.Width * 3 / 5; } catch (Exception) { }
            foreach (var e in host.Ctx.Events.Recent(100)) _recent.Items.Add(ToItem(e, null));
            RefreshPresent();
            RefreshReaders();
        };
    }

    private static ListView NewList()
    {
        var lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
        Ui.DoubleBuffer(lv);
        return lv;
    }

    private static ListViewItem ToItem(AttendanceEvent e, Color? color)
    {
        var item = new ListViewItem(new[]
        {
            e.Timestamp.Date == DateTime.Today ? e.Timestamp.ToString("HH:mm:ss") : e.Timestamp.ToString("dd.MM HH:mm"),
            Ui.Dir(e.Direction),
            e.EmployeeName ?? "NIEZNANA KARTA",
            e.EmployeeNo ?? "",
            e.CardUid,
            e.ReaderName ?? (e.Source == EventSource.Manual ? "wpis ręczny" : ""),
        }) { Tag = e };
        item.BackColor = color ?? (e.EmployeeId == null ? Ui.ErrorColor : e.Direction == Direction.In ? Ui.InColor : Ui.OutColor);
        return item;
    }

    private void OnScanned(ScanResult r)
    {
        _big.Text = r.Message;
        _sub.Text = $"{r.Time:HH:mm:ss} • {r.Reader.Name} • karta {r.CardUid}";
        _big.BackColor = _sub.BackColor = r.Status switch
        {
            ScanStatus.Registered => r.Direction == Direction.In ? Color.FromArgb(150, 220, 150) : Color.FromArgb(150, 190, 240),
            ScanStatus.Ignored => Ui.WarnColor,
            _ => Color.FromArgb(240, 150, 150),
        };
        _clearBig.Restart();
        if (r.Status != ScanStatus.Ignored) System.Media.SystemSounds.Asterisk.Play();
        else return;

        if (r.Event != null)
        {
            r.Event.EmployeeName = r.Employee?.FullName;
            r.Event.EmployeeNo = r.Employee?.EmployeeNo;
            r.Event.ReaderName = r.Reader.Name;
            r.Event.DepartmentName = r.Employee?.DepartmentName;
            _recent.Items.Insert(0, ToItem(r.Event, null));
            while (_recent.Items.Count > MaxRecent) _recent.Items.RemoveAt(_recent.Items.Count - 1);
        }
        if (!_presentDebounce.Enabled) _presentDebounce.Start();
    }

    private void RefreshPresent()
    {
        try
        {
            _presentAll = _host.Ctx.Events.CurrentlyPresent(DateTime.Now - _host.Ctx.MaxShift);
        }
        catch (Exception ex)
        {
            Log.Error("Lista obecnych", ex);
        }
        FillPresent();
    }

    private void FillPresent()
    {
        var filter = _presentFilter.Text.Trim();
        var rows = string.IsNullOrEmpty(filter)
            ? _presentAll
            : _presentAll.Where(e => (e.EmployeeName ?? "").Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                                     || (e.DepartmentName ?? "").Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();
        _present.BeginUpdate();
        _present.Items.Clear();
        foreach (var e in rows.Take(5000))
            _present.Items.Add(new ListViewItem(new[] { e.EmployeeName ?? "", e.DepartmentName ?? "", e.Timestamp.ToString("dd.MM HH:mm") }) { Tag = e });
        _present.EndUpdate();
        _presentBox.Text = $"Obecni w pracy: {_presentAll.Count}";
    }

    private void RefreshReaders()
    {
        _readers.SuspendLayout();
        _readers.Controls.Clear();
        if (_host.Readers.Drivers.Count == 0)
            _readers.Controls.Add(Ui.Label("Brak aktywnych czytników – skonfiguruj je w menu Konfiguracja → Czytniki."));
        foreach (var d in _host.Readers.Drivers)
        {
            _readers.Controls.Add(new Label
            {
                AutoSize = true,
                Text = $"{(d.IsOk ? "[OK]" : "[!]")} {d.Config.Name} ({ReadersForm.ModeName(d.Config.Mode)}): {d.Status}",
                ForeColor = d.IsOk ? Color.DarkGreen : Color.Firebrick,
                Margin = new Padding(6, 3, 12, 3),
            });
        }
        _readers.ResumeLayout();
    }

    private void OpenEmployee(ListView list)
    {
        if (list.SelectedItems.Count == 0 || list.SelectedItems[0].Tag is not AttendanceEvent { EmployeeId: { } id }) return;
        if (_host.Ctx.Employees.Get(id) is { } emp) ShowEmployee?.Invoke(emp);
    }

    private void AssignCard()
    {
        if (_recent.SelectedItems.Count == 0 || _recent.SelectedItems[0].Tag is not AttendanceEvent e) return;
        if (e.EmployeeId != null) { Ui.Info("Ta karta jest już przypisana do pracownika."); return; }
        if (Dialogs.PickEmployee(FindForm()!, _host.Ctx, $"Komu przypisać kartę {e.CardUid}?") is not { } emp) return;
        if (!string.IsNullOrEmpty(emp.CardUid) && !Ui.Confirm($"{emp.FullName} ma już kartę {emp.CardUid}. Zastąpić ją kartą {e.CardUid}?")) return;
        Ui.Try(() =>
        {
            emp.CardUid = e.CardUid;
            _host.Ctx.Employees.Save(emp);
            int n = _host.Ctx.Events.AssignUnknownCard(e.CardUid, emp.Id);
            _host.Ctx.Audit.Log("Przypisanie karty", $"{emp.EmployeeNo} karta={e.CardUid}, odbić: {n}");
            foreach (ListViewItem item in _recent.Items)
            {
                if (item.Tag is AttendanceEvent ev && ev.EmployeeId == null && ev.CardUid == e.CardUid)
                {
                    ev.EmployeeId = emp.Id;
                    ev.EmployeeName = emp.FullName;
                    ev.EmployeeNo = emp.EmployeeNo;
                    item.SubItems[2].Text = emp.FullName;
                    item.SubItems[3].Text = emp.EmployeeNo;
                    item.BackColor = ev.Direction == Direction.In ? Ui.InColor : Ui.OutColor;
                }
            }
            _host.NotifyDataChanged();
            Ui.Info($"Karta {e.CardUid} przypisana: {emp.FullName}. Zaktualizowano odbić: {n}.");
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _host.Scanned -= OnScanned;
            _host.DataChanged -= RefreshPresent;
            _host.Readers.StatusChanged -= RefreshReaders;
            _refresh.Dispose();
            _clearBig.Dispose();
            _presentDebounce.Dispose();
        }
        base.Dispose(disposing);
    }
}
