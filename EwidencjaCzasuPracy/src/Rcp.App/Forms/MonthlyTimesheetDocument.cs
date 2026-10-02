using System.Drawing.Printing;
using System.Globalization;
using Rcp.Core;

namespace Rcp.App.Forms;

/// <summary>Wydruk miesięcznej karty ewidencji czasu pracy pracownika.</summary>
internal sealed class MonthlyTimesheetDocument : PrintDocument
{
    private readonly string _company;
    private readonly Employee _employee;
    private readonly DateOnly _month;
    private readonly PeriodSummary _period;

    public MonthlyTimesheetDocument(string company, Employee employee, DateOnly month, PeriodSummary period)
    {
        _company = company;
        _employee = employee;
        _month = month;
        _period = period;
        DocumentName = $"Karta ewidencji {employee.FullName} {month:yyyy-MM}";
        DefaultPageSettings.Margins = new Margins(50, 50, 50, 50);
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        var g = e.Graphics!;
        var area = e.MarginBounds;
        using var title = new Font("Segoe UI", 14f, FontStyle.Bold);
        using var normal = new Font("Segoe UI", 9f);
        using var bold = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var pen = new Pen(Color.Gray, 0.5f);
        float y = area.Top;

        if (_company.Length > 0) { g.DrawString(_company, normal, Brushes.Black, area.Left, y); y += 18; }
        g.DrawString($"Karta ewidencji czasu pracy – {_month.ToString("MMMM yyyy", CultureInfo.CurrentCulture)}", title, Brushes.Black, area.Left, y);
        y += 30;
        g.DrawString($"Pracownik: {_employee.FullName}   Nr: {_employee.EmployeeNo}   Dział: {_employee.DepartmentName ?? "-"}   " +
                     $"Norma dobowa: {WorkTimeCalculator.FormatMinutes(_employee.DailyNormMinutes)}", normal, Brushes.Black, area.Left, y);
        y += 26;

        string[] headers = { "Dzień", "Wejście", "Wyjście", "Przepracowano", "Norma", "Nadgodziny", "Nieobecność / uwagi" };
        float[] widths = { 0.13f, 0.09f, 0.09f, 0.13f, 0.09f, 0.11f, 0.36f };
        float rowH = 19;

        void Row(string[] cells, Font font, Brush? bg)
        {
            float x = area.Left;
            for (int i = 0; i < cells.Length; i++)
            {
                var w = widths[i] * area.Width;
                var rect = new RectangleF(x, y, w, rowH);
                if (bg != null) g.FillRectangle(bg, rect);
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                g.DrawString(cells[i], font, Brushes.Black, new RectangleF(x + 3, y + 3, w - 6, rowH - 4),
                    new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                x += w;
            }
            y += rowH;
        }

        Row(headers, bold, Brushes.Gainsboro);
        foreach (var d in _period.Days)
        {
            var notes = new List<string>();
            if (d.Absence != null) notes.Add(AbsenceTypes.Name(d.Absence.Type));
            if (d.DayOffName != null) notes.Add(d.DayOffName);
            notes.AddRange(d.Anomalies);
            Row(new[]
            {
                d.Date.ToString("dd.MM ddd"),
                d.FirstIn?.ToString("HH:mm") ?? "",
                d.LastOut?.ToString("HH:mm") ?? "",
                d.WorkedMinutes > 0 ? WorkTimeCalculator.FormatMinutes(d.WorkedMinutes) : "",
                d.NormMinutes > 0 ? WorkTimeCalculator.FormatMinutes(d.NormMinutes) : "",
                d.OvertimeMinutes > 0 ? WorkTimeCalculator.FormatMinutes(d.OvertimeMinutes) : "",
                string.Join("; ", notes),
            }, normal, d.IsWorkingDay ? null : Brushes.WhiteSmoke);
        }

        y += 10;
        g.DrawString(
            $"Razem przepracowano: {WorkTimeCalculator.FormatMinutes(_period.WorkedMinutes)}    Norma: {WorkTimeCalculator.FormatMinutes(_period.NormMinutes)}    " +
            $"Nadgodziny: {WorkTimeCalculator.FormatMinutes(_period.OvertimeMinutes)}    Niedopracowanie: {WorkTimeCalculator.FormatMinutes(_period.ShortfallMinutes)}",
            bold, Brushes.Black, area.Left, y);
        y += 50;
        g.DrawString("....................................................\npodpis pracownika", normal, Brushes.Black, area.Left, y);
        g.DrawString("....................................................\npodpis przełożonego", normal, Brushes.Black, area.Right - 250, y);
        e.HasMorePages = false;
    }
}
