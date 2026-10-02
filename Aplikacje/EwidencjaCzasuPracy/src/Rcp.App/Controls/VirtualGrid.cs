namespace Rcp.App.Controls;

/// <summary>
/// Tabela w trybie wirtualnym – pobiera z bazy tylko widoczne strony wierszy,
/// dzięki czemu przewijanie setek tysięcy rekordów jest płynne.
/// </summary>
internal sealed class VirtualGrid<T> where T : class
{
    private const int PageSize = 200;
    private readonly List<Func<T, object?>> _values = new();
    private readonly Dictionary<int, List<T>> _pages = new();
    private Func<int> _count = () => 0;
    private Func<int, int, List<T>> _fetch = (_, _) => new List<T>();

    public DataGridView Grid { get; }
    public Func<T, Color?>? RowColor { get; set; }
    public event Action<T>? ItemActivated;

    public VirtualGrid()
    {
        Grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            VirtualMode = true,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToOrderColumns = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        };
        Grid.RowTemplate.Height = 24;
        Ui.DoubleBuffer(Grid);
        Grid.CellValueNeeded += (_, e) =>
        {
            var item = Get(e.RowIndex);
            if (item != null && e.ColumnIndex < _values.Count) e.Value = _values[e.ColumnIndex](item);
        };
        Grid.CellFormatting += (_, e) =>
        {
            if (RowColor == null || e.CellStyle == null) return;
            var item = Get(e.RowIndex);
            if (item != null && RowColor(item) is { } c) e.CellStyle.BackColor = c;
        };
        Grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && Get(e.RowIndex) is { } item) ItemActivated?.Invoke(item);
        };
        Grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && Selected is { } item) { ItemActivated?.Invoke(item); e.Handled = true; }
        };
    }

    public VirtualGrid<T> Column(string header, Func<T, object?> value, int width = 100, bool fill = false)
    {
        var col = new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            Width = width,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
        };
        if (fill) col.MinimumWidth = width;
        Grid.Columns.Add(col);
        _values.Add(value);
        return this;
    }

    public void SetSource(Func<int> count, Func<int, int, List<T>> fetch)
    {
        _count = count;
        _fetch = fetch;
        Reload();
    }

    public void SetList(IReadOnlyList<T> list)
        => SetSource(() => list.Count, (off, lim) => list.Skip(off).Take(lim).ToList());

    public void Reload()
    {
        var selected = Grid.CurrentCell?.RowIndex ?? 0;
        _pages.Clear();
        var n = _count();
        Grid.RowCount = 0;
        Grid.RowCount = n;
        if (n > 0)
        {
            try { Grid.CurrentCell = Grid.Rows[Math.Min(selected, n - 1)].Cells[0]; }
            catch (InvalidOperationException) { /* siatka jeszcze niewidoczna */ }
        }
        Grid.Invalidate();
    }

    public int Count => Grid.RowCount;

    public T? Get(int row)
    {
        if (row < 0 || row >= Grid.RowCount) return null;
        int page = row / PageSize;
        if (!_pages.TryGetValue(page, out var list))
        {
            if (_pages.Count > 50) _pages.Clear();
            list = _fetch(page * PageSize, PageSize);
            _pages[page] = list;
        }
        int idx = row % PageSize;
        return idx < list.Count ? list[idx] : null;
    }

    public T? Selected => Grid.CurrentCell is { RowIndex: >= 0 } c ? Get(c.RowIndex) : null;

    /// <summary>Wszystkie elementy (do eksportu) – pobierane stronami.</summary>
    public IEnumerable<T> All()
    {
        int n = _count();
        for (int off = 0; off < n; off += 1000)
            foreach (var x in _fetch(off, 1000)) yield return x;
    }
}
