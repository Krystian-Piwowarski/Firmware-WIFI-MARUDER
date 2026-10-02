using Rcp.Core;
using Rcp.Core.Data;
using Rcp.Core.Services;
using Xunit;

namespace Rcp.Tests;

public sealed class DatabaseTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rcp-test-{Guid.NewGuid():N}.db");
    private readonly RcpContext _ctx;

    public DatabaseTests() => _ctx = new RcpContext(_path);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" }) if (File.Exists(f)) File.Delete(f);
    }

    private Employee AddEmployee(string no, string last, string first, string? card = null, long? dep = null)
    {
        var e = new Employee { EmployeeNo = no, LastName = last, FirstName = first, CardUid = card, DepartmentId = dep };
        _ctx.Employees.Save(e);
        return e;
    }

    [Fact]
    public void SearchIsCaseInsensitiveWithPolishLettersAndPaged()
    {
        var dep = new Department { Name = "Produkcja" };
        _ctx.Departments.Save(dep);
        AddEmployee("P001", "Łukasiewicz", "Żaneta", "04A1", dep.Id);
        AddEmployee("P002", "Kowalski", "Jan", "04A2");
        for (int i = 0; i < 120; i++) AddEmployee($"X{i:000}", "Nowak", $"Imię{i}", dep: dep.Id);

        Assert.Single(_ctx.Employees.Search(new EmployeeQuery("łukas żan"), 0, 50));
        Assert.Single(_ctx.Employees.Search(new EmployeeQuery("04a2"), 0, 50));
        Assert.Equal(120, _ctx.Employees.Count(new EmployeeQuery("nowak")));
        Assert.Equal(121, _ctx.Employees.Count(new EmployeeQuery(null, dep.Id)));
        Assert.Equal(20, _ctx.Employees.Search(new EmployeeQuery("nowak"), 100, 50).Count);
        Assert.Equal(0, _ctx.Employees.Count(new EmployeeQuery("100%")));
    }

    [Fact]
    public void DuplicateCardGivesFriendlyError()
    {
        AddEmployee("1", "A", "A", "AABBCCDD");
        var ex = Assert.Throws<RcpException>(() => AddEmployee("2", "B", "B", "aa:bb:cc:dd"));
        Assert.Contains("A A", ex.Message);
    }

    [Fact]
    public void ScanProcessorTogglesDebouncesAndHandlesUnknownCards()
    {
        var emp = AddEmployee("1", "Kowalski", "Jan", "04A23B11");
        var reader = new ReaderConfig { Name = "Brama", Mode = ReaderMode.Toggle };
        _ctx.Readers.Save(reader);
        var p = new ScanProcessor(_ctx);
        var t = new DateTime(2026, 10, 5, 7, 0, 0);

        var r1 = p.Process(reader, "04:a2:3b:11", t);
        Assert.Equal(ScanStatus.Registered, r1.Status);
        Assert.Equal(Direction.In, r1.Direction);

        Assert.Equal(ScanStatus.Ignored, p.Process(reader, "04A23B11", t.AddSeconds(5)).Status);

        var r2 = p.Process(reader, "04A23B11", t.AddHours(8));
        Assert.Equal(Direction.Out, r2.Direction);

        var unknown = p.Process(reader, "FFFF0001", t.AddHours(8));
        Assert.Equal(ScanStatus.UnknownCard, unknown.Status);

        emp.CardUid = null;
        var other = AddEmployee("2", "Nowak", "Anna", "FFFF0001");
        Assert.Equal(1, _ctx.Events.AssignUnknownCard("FFFF0001", other.Id));

        var events = _ctx.Events.ForEmployee(emp.Id, t.Date, t.Date.AddDays(1));
        Assert.Equal(2, events.Count);
        Assert.Equal("Brama", events[0].ReaderName);
    }

    [Fact]
    public void CurrentlyPresentAndMonthlyReport()
    {
        var a = AddEmployee("1", "A", "Adam", "01");
        var b = AddEmployee("2", "B", "Bartek", "02");
        var t = new DateTime(2026, 10, 5, 7, 0, 0);
        void Ev(Employee e, DateTime ts, Direction d) =>
            _ctx.Events.Insert(new AttendanceEvent { EmployeeId = e.Id, CardUid = e.CardUid!, Timestamp = ts, Direction = d });

        Ev(a, t, Direction.In);
        Ev(a, t.AddHours(8), Direction.Out);
        Ev(b, t, Direction.In);

        var present = _ctx.Events.CurrentlyPresent(t.AddHours(-16));
        Assert.Single(present);
        Assert.Equal(b.Id, present[0].EmployeeId);

        var rows = new ReportService(_ctx).Summary(new(2026, 10, 1), new(2026, 10, 31), null, null, t.AddHours(9));
        Assert.Equal(2, rows.Count);
        Assert.Equal(480, rows.Single(r => r.Employee.Id == a.Id).Summary.WorkedMinutes);
        Assert.True(rows.Single(r => r.Employee.Id == b.Id).Summary.Days[4].InProgress);
    }

    [Fact]
    public void AbsencesCannotOverlap()
    {
        var e = AddEmployee("1", "A", "A");
        _ctx.Absences.Save(new Absence { EmployeeId = e.Id, DateFrom = new(2026, 10, 5), DateTo = new(2026, 10, 9), Type = AbsenceType.Urlop });
        Assert.Throws<RcpException>(() => _ctx.Absences.Save(new Absence
            { EmployeeId = e.Id, DateFrom = new(2026, 10, 9), DateTo = new(2026, 10, 12), Type = AbsenceType.L4 }));
        Assert.Single(_ctx.Absences.ForEmployee(e.Id, new(2026, 10, 1), new(2026, 10, 31)));
    }

    [Fact]
    public void CsvImportUpsertsAndCreatesDepartments()
    {
        var csv = Path.Combine(Path.GetTempPath(), $"rcp-{Guid.NewGuid():N}.csv");
        File.WriteAllText(csv, "Nr;Nazwisko;Imię;Dział;Stanowisko;Karta;Norma_h\n" +
                               "100;Kowalski;Jan;Magazyn;Magazynier;04A1;8\n" +
                               "101;\"Nowak; Anna\";Ewa;Biuro;;04a2;7,5\n");
        try
        {
            var (ins, upd, errors) = _ctx.Employees.Upsert(EmployeeCsv.Parse(csv));
            Assert.Equal((2, 0), (ins, upd));
            Assert.Empty(errors);
            Assert.Equal(450, _ctx.Employees.GetByCard("04A2")!.DailyNormMinutes);
            Assert.Equal(2, _ctx.Departments.All().Count);

            (ins, upd, _) = _ctx.Employees.Upsert(EmployeeCsv.Parse(csv));
            Assert.Equal((0, 2), (ins, upd));
        }
        finally { File.Delete(csv); }
    }

    [Fact]
    public void SettingsRoundTrip()
    {
        _ctx.SaveSettings(new AppSettings { DebounceSeconds = 5, MaxShiftHours = 12, CompanyName = "ACME" });
        var s = new SettingsRepository(_ctx.Db).Load();
        Assert.Equal((5, 12, "ACME"), (s.DebounceSeconds, s.MaxShiftHours, s.CompanyName));
    }
}
