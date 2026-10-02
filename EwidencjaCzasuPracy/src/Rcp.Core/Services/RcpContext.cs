using Rcp.Core.Data;

namespace Rcp.Core.Services;

/// <summary>Punkt dostępu do wszystkich repozytoriów i usług aplikacji.</summary>
public sealed class RcpContext
{
    public Database Db { get; }
    public DepartmentRepository Departments { get; }
    public EmployeeRepository Employees { get; }
    public EventRepository Events { get; }
    public AbsenceRepository Absences { get; }
    public ReaderRepository Readers { get; }
    public CalendarRepository Calendar { get; }
    public SettingsRepository SettingsRepo { get; }
    public AuditRepository Audit { get; }
    public AppSettings Settings { get; private set; }

    public RcpContext(string databasePath)
    {
        Db = new Database(databasePath);
        Db.Initialize();
        Departments = new DepartmentRepository(Db);
        Employees = new EmployeeRepository(Db);
        Events = new EventRepository(Db);
        Absences = new AbsenceRepository(Db);
        Readers = new ReaderRepository(Db);
        Calendar = new CalendarRepository(Db);
        SettingsRepo = new SettingsRepository(Db);
        Audit = new AuditRepository(Db);
        Settings = SettingsRepo.Load();
    }

    public void SaveSettings(AppSettings s)
    {
        SettingsRepo.Save(s);
        Settings = s;
    }

    public TimeSpan MaxShift => TimeSpan.FromHours(Math.Max(1, Settings.MaxShiftHours));
}
