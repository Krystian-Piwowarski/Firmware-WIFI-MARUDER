using Rcp.Core.Data;

namespace Rcp.Core.Services;

public sealed class EmployeeMonthRow
{
    public required Employee Employee { get; init; }
    public required PeriodSummary Summary { get; init; }
}

public sealed class ReportService(RcpContext ctx)
{
    /// <summary>Podsumowanie jednego pracownika w zadanym okresie (kalendarz, karta ewidencji).</summary>
    public PeriodSummary ForEmployee(Employee employee, DateOnly from, DateOnly to, DateTime now)
    {
        var start = from.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(2).ToDateTime(TimeOnly.MinValue);
        var events = ctx.Events.ForEmployee(employee.Id, start, end).Select(e => (e.Timestamp, e.Direction));
        var absences = ctx.Absences.ForEmployee(employee.Id, from, to);
        return WorkTimeCalculator.Summarize(employee.DailyNormMinutes, from, to, events, absences,
            ctx.Calendar.Load(), ctx.MaxShift, now);
    }

    /// <summary>Zestawienie zbiorcze – wszyscy (lub dział) za okres. Zdarzenia ładowane jednym zapytaniem.</summary>
    public List<EmployeeMonthRow> Summary(DateOnly from, DateOnly to, long? departmentId, string? text, DateTime now,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var calendar = ctx.Calendar.Load();
        var events = ctx.Events.ByEmployee(from.AddDays(-1).ToDateTime(TimeOnly.MinValue), to.AddDays(2).ToDateTime(TimeOnly.MinValue), departmentId);
        var absences = ctx.Absences.ByEmployee(from, to);
        var rows = new List<EmployeeMonthRow>();
        int n = 0;

        foreach (var emp in ctx.Employees.StreamAll(new EmployeeQuery(text, departmentId, true)))
        {
            ct.ThrowIfCancellationRequested();
            var summary = WorkTimeCalculator.Summarize(emp.DailyNormMinutes, from, to,
                events.TryGetValue(emp.Id, out var ev) ? ev : new List<(DateTime, Direction)>(),
                absences.TryGetValue(emp.Id, out var ab) ? ab : new List<Absence>(),
                calendar, ctx.MaxShift, now);
            rows.Add(new EmployeeMonthRow { Employee = emp, Summary = summary });
            if (++n % 500 == 0) progress?.Report(n);
        }
        progress?.Report(n);
        return rows;
    }
}
