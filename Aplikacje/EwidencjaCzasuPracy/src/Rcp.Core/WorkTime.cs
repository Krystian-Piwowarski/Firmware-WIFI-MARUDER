namespace Rcp.Core;

/// <summary>Para wejście–wyjście. Brak jednej ze stron oznacza anomalię (lub trwającą zmianę).</summary>
public sealed record WorkSession(DateTime? In, DateTime? Out)
{
    public DateOnly Date => DateOnly.FromDateTime((In ?? Out)!.Value);
    public bool IsComplete => In.HasValue && Out.HasValue;
    public int Minutes => IsComplete ? (int)(Out!.Value - In!.Value).TotalMinutes : 0;
}

public sealed class DaySummary
{
    public DateOnly Date { get; init; }
    public bool IsWorkingDay { get; init; }
    public string? DayOffName { get; init; }
    public Absence? Absence { get; init; }
    public int NormMinutes { get; init; }
    public List<WorkSession> Sessions { get; } = new();
    public List<string> Anomalies { get; } = new();
    /// <summary>Pracownik jest teraz w pracy (otwarte wejście bez wyjścia, w granicach zmiany).</summary>
    public bool InProgress { get; set; }

    public int WorkedMinutes => Sessions.Sum(s => s.Minutes);
    public int OvertimeMinutes => Math.Max(0, WorkedMinutes - NormMinutes);
    public DateTime? FirstIn => Sessions.Where(s => s.In.HasValue).Select(s => s.In).Min();
    public DateTime? LastOut => Sessions.Where(s => s.Out.HasValue).Select(s => s.Out).Max();
    public bool HasAnomaly => Anomalies.Count > 0;
}

public sealed class PeriodSummary
{
    public required List<DaySummary> Days { get; init; }
    public int WorkedMinutes => Days.Sum(d => d.WorkedMinutes);
    public int NormMinutes => Days.Sum(d => d.NormMinutes);
    public int OvertimeMinutes => Days.Sum(d => d.OvertimeMinutes);
    /// <summary>Niedopracowanie liczone tylko z dni zakończonych.</summary>
    public int ShortfallMinutes { get; init; }
    public int DaysPresent => Days.Count(d => d.Sessions.Count > 0);
    public int AnomalyCount => Days.Sum(d => d.Anomalies.Count);
    public Dictionary<AbsenceType, int> AbsenceDays => Days.Where(d => d.Absence != null)
        .GroupBy(d => d.Absence!.Type).ToDictionary(g => g.Key, g => g.Count());
}

public static class WorkTimeCalculator
{
    /// <summary>Paruje zdarzenia wejścia/wyjścia w sesje pracy (obsługuje zmiany nocne).</summary>
    public static List<WorkSession> BuildSessions(IEnumerable<(DateTime Time, Direction Dir)> events, TimeSpan maxShift)
    {
        var sessions = new List<WorkSession>();
        DateTime? open = null;

        foreach (var (time, dir) in events.OrderBy(e => e.Time))
        {
            if (dir == Direction.In)
            {
                if (open.HasValue)
                {
                    // dwa wejścia pod rząd: jeśli bardzo blisko – traktujemy jak duplikat
                    if (time - open.Value < TimeSpan.FromMinutes(2)) continue;
                    sessions.Add(new WorkSession(open, null));
                }
                open = time;
            }
            else
            {
                if (open.HasValue && time - open.Value <= maxShift)
                {
                    sessions.Add(new WorkSession(open, time));
                    open = null;
                }
                else
                {
                    if (open.HasValue) { sessions.Add(new WorkSession(open, null)); open = null; }
                    // wyjście tuż po poprzednim wyjściu – duplikat
                    var last = sessions.LastOrDefault();
                    if (last?.Out is { } prevOut && time - prevOut < TimeSpan.FromMinutes(2)) continue;
                    sessions.Add(new WorkSession(null, time));
                }
            }
        }
        if (open.HasValue) sessions.Add(new WorkSession(open, null));
        return sessions;
    }

    /// <summary>
    /// Wylicza dzienne podsumowania. <paramref name="events"/> powinny obejmować także dzień przed i po okresie,
    /// aby poprawnie sparować zmiany nocne.
    /// </summary>
    public static PeriodSummary Summarize(
        int dailyNormMinutes,
        DateOnly from, DateOnly to,
        IEnumerable<(DateTime Time, Direction Dir)> events,
        IEnumerable<Absence> absences,
        WorkCalendar calendar,
        TimeSpan maxShift,
        DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var absenceList = absences.ToList();
        var sessions = BuildSessions(events, maxShift);
        var byDate = sessions.GroupBy(s => s.Date).ToDictionary(g => g.Key, g => g.ToList());

        var days = new List<DaySummary>();
        int shortfall = 0;

        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var dayOff = calendar.DayOffName(d);
            bool working = calendar.IsWorkingDay(d);
            var absence = absenceList.FirstOrDefault(a => a.Covers(d));
            int norm = working && absence == null ? dailyNormMinutes : 0;

            var day = new DaySummary
            {
                Date = d,
                IsWorkingDay = working,
                DayOffName = dayOff,
                Absence = absence,
                NormMinutes = norm,
            };

            if (byDate.TryGetValue(d, out var list))
            {
                day.Sessions.AddRange(list);
                foreach (var s in list)
                {
                    if (s.In is { } i && s.Out is null)
                    {
                        if (now - i <= maxShift && now >= i) day.InProgress = true;
                        else day.Anomalies.Add($"Brak wyjścia po wejściu {i:HH:mm}");
                    }
                    else if (s.In is null && s.Out is { } o)
                    {
                        day.Anomalies.Add($"Brak wejścia przed wyjściem {o:HH:mm}");
                    }
                }
            }
            else if (norm > 0 && d < today)
            {
                day.Anomalies.Add("Nieobecność nieusprawiedliwiona");
            }

            if (d < today) shortfall += Math.Max(0, day.NormMinutes - day.WorkedMinutes);
            days.Add(day);
        }

        return new PeriodSummary { Days = days, ShortfallMinutes = shortfall };
    }

    public static string FormatMinutes(int minutes)
    {
        var sign = minutes < 0 ? "-" : "";
        minutes = Math.Abs(minutes);
        return $"{sign}{minutes / 60}:{minutes % 60:00}";
    }
}
