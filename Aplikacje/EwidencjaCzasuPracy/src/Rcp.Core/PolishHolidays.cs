namespace Rcp.Core;

/// <summary>Ustawowe dni wolne od pracy w Polsce.</summary>
public static class PolishHolidays
{
    private static readonly Dictionary<int, Dictionary<DateOnly, string>> Cache = new();
    private static readonly object Lock = new();

    public static DateOnly EasterSunday(int year)
    {
        // algorytm Meeusa/Jonesa/Butchera (kalendarz gregoriański)
        int a = year % 19, b = year / 100, c = year % 100;
        int d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4, k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;
        int day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }

    public static IReadOnlyDictionary<DateOnly, string> ForYear(int year)
    {
        lock (Lock)
        {
            if (Cache.TryGetValue(year, out var cached)) return cached;

            var easter = EasterSunday(year);
            var list = new Dictionary<DateOnly, string>
            {
                [new DateOnly(year, 1, 1)] = "Nowy Rok",
                [new DateOnly(year, 1, 6)] = "Trzech Króli",
                [easter] = "Wielkanoc",
                [easter.AddDays(1)] = "Poniedziałek Wielkanocny",
                [new DateOnly(year, 5, 1)] = "Święto Pracy",
                [new DateOnly(year, 5, 3)] = "Święto Konstytucji 3 Maja",
                [easter.AddDays(49)] = "Zielone Świątki",
                [easter.AddDays(60)] = "Boże Ciało",
                [new DateOnly(year, 8, 15)] = "Wniebowzięcie NMP",
                [new DateOnly(year, 11, 1)] = "Wszystkich Świętych",
                [new DateOnly(year, 11, 11)] = "Święto Niepodległości",
                [new DateOnly(year, 12, 25)] = "Boże Narodzenie",
                [new DateOnly(year, 12, 26)] = "Drugi dzień Bożego Narodzenia",
            };
            if (year >= 2025) list[new DateOnly(year, 12, 24)] = "Wigilia";

            Cache[year] = list;
            return list;
        }
    }

    public static string? NameOf(DateOnly d) => ForYear(d.Year).TryGetValue(d, out var n) ? n : null;
}

/// <summary>Kalendarz dni roboczych: weekendy, święta ustawowe i dni wolne firmy.</summary>
public sealed class WorkCalendar
{
    private readonly Dictionary<DateOnly, string> _companyDaysOff;

    public WorkCalendar(IEnumerable<CompanyDayOff>? companyDaysOff = null)
    {
        _companyDaysOff = (companyDaysOff ?? Enumerable.Empty<CompanyDayOff>())
            .GroupBy(d => d.Date).ToDictionary(g => g.Key, g => g.First().Name);
    }

    /// <summary>Zwraca nazwę święta / dnia wolnego albo null, jeśli to zwykły dzień.</summary>
    public string? DayOffName(DateOnly d)
        => PolishHolidays.NameOf(d) ?? (_companyDaysOff.TryGetValue(d, out var n) ? n : null);

    public bool IsWorkingDay(DateOnly d)
        => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && DayOffName(d) is null;

    public int WorkingDays(DateOnly from, DateOnly to)
    {
        int n = 0;
        for (var d = from; d <= to; d = d.AddDays(1)) if (IsWorkingDay(d)) n++;
        return n;
    }
}
