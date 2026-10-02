using System.Globalization;
using Rcp.Core;

namespace Rcp.App.Controls;

/// <summary>Kalendarz miesięczny pracownika: przepracowane godziny, nieobecności, święta i anomalie.</summary>
internal sealed class MonthView : Control
{
    private static readonly string[] DayNames = { "Pn", "Wt", "Śr", "Cz", "Pt", "So", "Nd" };
    private Dictionary<DateOnly, DaySummary> _days = new();
    private DateOnly _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateOnly _selected = DateOnly.FromDateTime(DateTime.Today);
    private readonly ToolTip _tip = new();
    private DateOnly? _hover;

    public event Action<DateOnly>? DateSelected;
    public event Action<DateOnly>? DateActivated;

    public MonthView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = SystemColors.Window;
        Font = new Font("Segoe UI", 9f);
        TabStop = true;
    }

    public DateOnly Month
    {
        get => _month;
        set { _month = new DateOnly(value.Year, value.Month, 1); Invalidate(); }
    }

    public DateOnly Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            if (value.Year != _month.Year || value.Month != _month.Month) _month = new DateOnly(value.Year, value.Month, 1);
            Invalidate();
            DateSelected?.Invoke(value);
        }
    }

    public void SetData(IEnumerable<DaySummary> days)
    {
        _days = days.ToDictionary(d => d.Date);
        Invalidate();
    }

    private DateOnly FirstCell => _month.AddDays(-(((int)_month.DayOfWeek + 6) % 7));
    private const int HeaderHeight = 26;

    private Rectangle CellRect(int index)
    {
        float w = (Width - 1) / 7f, h = (Height - HeaderHeight - 1) / 6f;
        int col = index % 7, row = index / 7;
        return Rectangle.Round(new RectangleF(col * w, HeaderHeight + row * h, w, h));
    }

    private DateOnly? HitTest(Point p)
    {
        if (p.Y < HeaderHeight) return null;
        int col = (int)(p.X / ((Width - 1) / 7f)), row = (int)((p.Y - HeaderHeight) / ((Height - HeaderHeight - 1) / 6f));
        if (col is < 0 or > 6 || row is < 0 or > 5) return null;
        return FirstCell.AddDays(row * 7 + col);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var today = DateOnly.FromDateTime(DateTime.Today);
        using var small = new Font(Font.FontFamily, Font.Size - 1);
        using var big = new Font(Font.FontFamily, Font.Size + 3, FontStyle.Bold);
        using var bold = new Font(Font, FontStyle.Bold);
        using var gridPen = new Pen(Color.FromArgb(210, 210, 210));

        float w = (Width - 1) / 7f;
        for (int i = 0; i < 7; i++)
        {
            var r = new RectangleF(i * w, 0, w, HeaderHeight);
            TextRenderer.DrawText(g, DayNames[i], bold, Rectangle.Round(r), i >= 5 ? Color.Firebrick : ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        for (int i = 0; i < 42; i++)
        {
            var date = FirstCell.AddDays(i);
            var rect = CellRect(i);
            bool inMonth = date.Month == _month.Month;
            _days.TryGetValue(date, out var day);

            Color bg = SystemColors.Window;
            if (day?.Absence != null) bg = Color.FromArgb(255, 243, 205);
            else if (day != null && !day.IsWorkingDay) bg = Color.FromArgb(242, 242, 242);
            else if (day is null && date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) bg = Color.FromArgb(242, 242, 242);
            if (date == _selected) bg = Color.FromArgb(204, 228, 247);
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, rect);
            g.DrawRectangle(gridPen, rect);

            if (day?.HasAnomaly == true)
            {
                using var p = new Pen(Color.Firebrick, 2);
                g.DrawRectangle(p, Rectangle.Inflate(rect, -2, -2));
            }
            if (date == today)
            {
                using var p = new Pen(Color.RoyalBlue, 2);
                g.DrawRectangle(p, Rectangle.Inflate(rect, -1, -1));
            }

            var fore = inMonth ? ForeColor : Color.Silver;
            var dayColor = day?.DayOffName != null || date.DayOfWeek == DayOfWeek.Sunday ? Color.Firebrick : fore;
            TextRenderer.DrawText(g, date.Day.ToString(CultureInfo.InvariantCulture), bold, new Point(rect.X + 4, rect.Y + 3),
                inMonth ? dayColor : Color.Silver);

            if (!inMonth || day == null) continue;

            int y = rect.Y + 3;
            if (day.Absence != null)
            {
                var code = AbsenceTypes.Code(day.Absence.Type);
                var size = TextRenderer.MeasureText(code, bold);
                var badge = new Rectangle(rect.Right - size.Width - 8, y, size.Width + 4, size.Height);
                using (var b = new SolidBrush(Color.DarkOrange)) g.FillRectangle(b, badge);
                TextRenderer.DrawText(g, code, bold, badge, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            var centerRect = new Rectangle(rect.X, rect.Y + rect.Height / 2 - 14, rect.Width, 26);
            if (day.WorkedMinutes > 0)
                TextRenderer.DrawText(g, WorkTimeCalculator.FormatMinutes(day.WorkedMinutes), big, centerRect,
                    day.OvertimeMinutes > 0 ? Color.DarkGreen : fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            else if (day.InProgress)
                TextRenderer.DrawText(g, "w pracy", bold, centerRect, Color.SeaGreen, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            var bottom = new Rectangle(rect.X + 3, rect.Bottom - 18, rect.Width - 6, 16);
            string? info = day.DayOffName
                ?? (day.FirstIn is { } fi ? $"{fi:HH:mm}–{(day.LastOut is { } lo ? lo.ToString("HH:mm") : "…")}" : null);
            if (day.HasAnomaly) info = "! " + (info ?? day.Anomalies[0]);
            if (info != null)
                TextRenderer.DrawText(g, info, small, bottom, day.HasAnomaly ? Color.Firebrick : Color.DimGray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        }

        if (Focused) ControlPaint.DrawFocusRectangle(g, new Rectangle(0, 0, Width, Height));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (HitTest(e.Location) is { } d) Selected = d;
        base.OnMouseDown(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (HitTest(e.Location) is { } d) DateActivated?.Invoke(d);
        base.OnMouseDoubleClick(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var d = HitTest(e.Location);
        if (d != _hover)
        {
            _hover = d;
            _tip.SetToolTip(this, d is { } date && _days.TryGetValue(date, out var day) ? Describe(day) : null);
        }
        base.OnMouseMove(e);
    }

    public static string Describe(DaySummary day)
    {
        var lines = new List<string> { day.Date.ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture) };
        if (day.DayOffName != null) lines.Add(day.DayOffName);
        if (day.Absence != null) lines.Add(AbsenceTypes.Name(day.Absence.Type) + (day.Absence.Note is { Length: > 0 } n ? $" – {n}" : ""));
        lines.Add($"Przepracowano: {WorkTimeCalculator.FormatMinutes(day.WorkedMinutes)} / norma {WorkTimeCalculator.FormatMinutes(day.NormMinutes)}");
        if (day.OvertimeMinutes > 0) lines.Add($"Nadgodziny: {WorkTimeCalculator.FormatMinutes(day.OvertimeMinutes)}");
        foreach (var s in day.Sessions)
            lines.Add($"  {s.In?.ToString("HH:mm") ?? "??:??"} – " + (s.Out is { } o
                ? o.ToString(DateOnly.FromDateTime(o) == day.Date ? "HH:mm" : "dd.MM HH:mm")
                : day.InProgress ? "trwa" : "??:??"));
        lines.AddRange(day.Anomalies.Select(a => "UWAGA: " + a));
        return string.Join(Environment.NewLine, lines);
    }

    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int delta = e.KeyCode switch { Keys.Left => -1, Keys.Right => 1, Keys.Up => -7, Keys.Down => 7, _ => 0 };
        if (delta != 0) Selected = _selected.AddDays(delta);
        if (e.KeyCode == Keys.Enter) DateActivated?.Invoke(_selected);
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
}
