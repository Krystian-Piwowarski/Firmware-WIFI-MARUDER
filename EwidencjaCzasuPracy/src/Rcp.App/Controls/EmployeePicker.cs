using Rcp.Core;
using Rcp.Core.Data;
using Rcp.Core.Services;

namespace Rcp.App.Controls;

/// <summary>Pole wyszukiwania pracownika z listą wyników (do wyboru z bardzo dużej bazy).</summary>
internal sealed class EmployeePicker : UserControl
{
    private const int MaxResults = 200;
    private readonly RcpContext _ctx;
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Szukaj: nazwisko, imię, nr, karta…" };
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label _info = new() { Dock = DockStyle.Bottom, Height = 20, ForeColor = Color.DimGray };
    private readonly System.Windows.Forms.Timer _debounce;

    public event Action<Employee?>? SelectionChanged;
    public event Action<Employee>? Activated;

    public bool IncludeInactive { get; set; }

    public EmployeePicker(RcpContext ctx)
    {
        _ctx = ctx;
        _debounce = Ui.Debouncer(250, RunSearch);
        Controls.Add(_list);
        Controls.Add(_search);
        Controls.Add(_info);
        _search.TextChanged += (_, _) => _debounce.Restart();
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down && _list.Items.Count > 0)
            {
                _list.Focus();
                _list.SelectedIndex = Math.Max(0, _list.SelectedIndex);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                _debounce.Stop();
                RunSearch();
                if (_list.Items.Count == 1) { _list.SelectedIndex = 0; if (Selected is { } emp) Activated?.Invoke(emp); }
                e.SuppressKeyPress = true;
            }
        };
        _list.SelectedIndexChanged += (_, _) => SelectionChanged?.Invoke(Selected);
        _list.DoubleClick += (_, _) => { if (Selected is { } e) Activated?.Invoke(e); };
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && Selected is { } emp) { Activated?.Invoke(emp); e.Handled = true; } };
        Load += (_, _) => RunSearch();
    }

    public Employee? Selected => _list.SelectedItem as Employee;

    public void FocusSearch() => _search.Focus();

    public string SearchText
    {
        get => _search.Text;
        set => _search.Text = value;
    }

    public void RunSearch()
    {
        var q = new EmployeeQuery(_search.Text, null, IncludeInactive ? null : true);
        var list = _ctx.Employees.Search(q, 0, MaxResults);
        var prev = Selected?.Id;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var e in list) _list.Items.Add(e);
        _list.EndUpdate();
        if (prev != null)
        {
            var idx = list.FindIndex(e => e.Id == prev);
            if (idx >= 0) _list.SelectedIndex = idx;
        }
        _info.Text = list.Count >= MaxResults ? $"Pokazano {MaxResults} pierwszych – zawęź wyszukiwanie" : $"Znaleziono: {list.Count}";
    }

    public void Select(Employee e)
    {
        _search.Text = e.EmployeeNo;
        _debounce.Stop();
        RunSearch();
        for (int i = 0; i < _list.Items.Count; i++)
            if (_list.Items[i] is Employee x && x.Id == e.Id) { _list.SelectedIndex = i; break; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _debounce.Dispose();
        base.Dispose(disposing);
    }
}
