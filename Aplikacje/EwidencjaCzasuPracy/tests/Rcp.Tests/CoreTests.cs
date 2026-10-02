using Rcp.Core;
using Xunit;

namespace Rcp.Tests;

public class CardUidTests
{
    [Theory]
    [InlineData("04:a2:3b:11", false, false, "04A23B11")]
    [InlineData(" 04 A2 3B 11 ", false, false, "04A23B11")]
    [InlineData("0001234567", true, false, "12D687")]
    [InlineData("04A23B11", false, true, "113BA204")]
    [InlineData("123", true, false, "7B")]
    public void Normalize(string raw, bool dec, bool rev, string expected)
        => Assert.Equal(expected, CardUid.Normalize(raw, dec, rev));

    [Theory]
    [InlineData("04A23B11", "04A23B11")]
    [InlineData("UID: 04A23B11", "04A23B11")]
    [InlineData("CARD=1234567890", "1234567890")]
    [InlineData("04:A2:3B:11", "04A23B11")]
    [InlineData("hello", null)]
    [InlineData("", null)]
    public void ExtractUid(string line, string? expected)
        => Assert.Equal(expected, LineCardParser.ExtractUid(line));

    [Fact]
    public void Parser_HandlesStxEtxAndCrLf()
    {
        var p = new LineCardParser();
        var t = DateTime.Now;
        string? got = null;
        foreach (var ch in "\u000204A23B11\u0003") got ??= p.Feed(ch, t);
        Assert.Equal("04A23B11", got);

        got = null;
        foreach (var ch in "DEADBEEF\r\n") got ??= p.Feed(ch, t);
        Assert.Equal("DEADBEEF", got);
    }

    [Fact]
    public void Parser_FlushesOnIdle()
    {
        var p = new LineCardParser();
        var t = DateTime.Now;
        foreach (var ch in "A1B2C3D4") Assert.Null(p.Feed(ch, t));
        Assert.Null(p.FlushIfIdle(t.AddMilliseconds(50)));
        Assert.Equal("A1B2C3D4", p.FlushIfIdle(t.AddSeconds(1)));
    }
}

public class HolidayTests
{
    [Theory]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    public void Easter(int y, int m, int d) => Assert.Equal(new DateOnly(y, m, d), PolishHolidays.EasterSunday(y));

    [Fact]
    public void CorpusChristi2026() => Assert.Equal("Boże Ciało", PolishHolidays.NameOf(new DateOnly(2026, 6, 4)));

    [Fact]
    public void ChristmasEveFrom2025()
    {
        Assert.Null(PolishHolidays.NameOf(new DateOnly(2024, 12, 24)));
        Assert.NotNull(PolishHolidays.NameOf(new DateOnly(2025, 12, 24)));
    }

    [Fact]
    public void WorkingDaysInNovember2026()
    {
        // listopad 2026: 21 dni pon-pt, minus 11.11 (środa); 1.11 to niedziela
        var cal = new WorkCalendar();
        Assert.Equal(20, cal.WorkingDays(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)));
    }

    [Fact]
    public void CompanyDayOff()
    {
        var cal = new WorkCalendar(new[] { new CompanyDayOff { Date = new DateOnly(2026, 11, 10), Name = "Za 31.10" } });
        Assert.False(cal.IsWorkingDay(new DateOnly(2026, 11, 10)));
        Assert.Equal("Za 31.10", cal.DayOffName(new DateOnly(2026, 11, 10)));
    }
}

public class WorkTimeTests
{
    private static readonly TimeSpan Shift = TimeSpan.FromHours(16);
    private static DateTime T(int day, int h, int m = 0) => new(2026, 10, day, h, m, 0);

    [Fact]
    public void SimpleDay()
    {
        var s = WorkTimeCalculator.BuildSessions(new[] { (T(5, 7), Direction.In), (T(5, 15, 30), Direction.Out) }, Shift);
        Assert.Single(s);
        Assert.Equal(510, s[0].Minutes);
    }

    [Fact]
    public void NightShiftCountsForStartDay()
    {
        var s = WorkTimeCalculator.BuildSessions(new[] { (T(5, 22), Direction.In), (T(6, 6), Direction.Out) }, Shift);
        Assert.Single(s);
        Assert.Equal(new DateOnly(2026, 10, 5), s[0].Date);
        Assert.Equal(480, s[0].Minutes);
    }

    [Fact]
    public void MissingOutAndOrphanOut()
    {
        var s = WorkTimeCalculator.BuildSessions(new[]
        {
            (T(5, 7), Direction.In),
            (T(6, 7), Direction.In),
            (T(6, 15), Direction.Out),
            (T(7, 15), Direction.Out),
        }, Shift);
        Assert.Equal(3, s.Count);
        Assert.Null(s[0].Out);
        Assert.Equal(480, s[1].Minutes);
        Assert.Null(s[2].In);
    }

    [Fact]
    public void DuplicateInIsIgnored()
    {
        var s = WorkTimeCalculator.BuildSessions(new[] { (T(5, 7), Direction.In), (T(5, 7, 1), Direction.In), (T(5, 15), Direction.Out) }, Shift);
        Assert.Single(s);
        Assert.Equal(480, s[0].Minutes);
    }

    [Fact]
    public void SummaryWithOvertimeAbsenceAndAnomaly()
    {
        // 5.10.2026 poniedziałek
        var events = new[]
        {
            (T(5, 6), Direction.In), (T(5, 16), Direction.Out),   // 10h → 2h nadgodzin
            (T(6, 8), Direction.In),                                // brak wyjścia
            (T(10, 8), Direction.In), (T(10, 12), Direction.Out),  // sobota – całość nadgodziny
        };
        var absences = new[] { new Absence { DateFrom = new(2026, 10, 7), DateTo = new(2026, 10, 9), Type = AbsenceType.Urlop } };
        var sum = WorkTimeCalculator.Summarize(480, new(2026, 10, 5), new(2026, 10, 11), events, absences,
            new WorkCalendar(), Shift, new DateTime(2026, 10, 20));

        Assert.Equal(7, sum.Days.Count);
        Assert.Equal(840, sum.WorkedMinutes);
        Assert.Equal(2 * 480, sum.NormMinutes); // pn, wt (śr–pt urlop, weekend)
        Assert.Equal(120 + 240, sum.OvertimeMinutes);
        Assert.Equal(480, sum.ShortfallMinutes); // wtorek bez wyjścia
        Assert.Contains(sum.Days[1].Anomalies, a => a.Contains("Brak wyjścia"));
        Assert.Equal(3, sum.AbsenceDays[AbsenceType.Urlop]);
    }

    [Fact]
    public void OpenSessionTodayIsInProgressNotAnomaly()
    {
        var sum = WorkTimeCalculator.Summarize(480, new(2026, 10, 5), new(2026, 10, 5),
            new[] { (T(5, 7), Direction.In) }, Array.Empty<Absence>(), new WorkCalendar(), Shift, T(5, 10));
        Assert.True(sum.Days[0].InProgress);
        Assert.Empty(sum.Days[0].Anomalies);
    }

    [Fact]
    public void UnexcusedAbsence()
    {
        var sum = WorkTimeCalculator.Summarize(480, new(2026, 10, 5), new(2026, 10, 5),
            Array.Empty<(DateTime, Direction)>(), Array.Empty<Absence>(), new WorkCalendar(), Shift, T(8, 10));
        Assert.Contains("Nieobecność nieusprawiedliwiona", sum.Days[0].Anomalies);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(485, "8:05")]
    [InlineData(-90, "-1:30")]
    public void Format(int m, string s) => Assert.Equal(s, WorkTimeCalculator.FormatMinutes(m));
}
