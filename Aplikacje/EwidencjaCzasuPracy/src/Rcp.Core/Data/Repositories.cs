using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Rcp.Core.Data;

/// <summary>Błąd czytelny dla użytkownika (np. zduplikowany numer karty).</summary>
public sealed class RcpException(string message) : Exception(message);

public sealed record EmployeeQuery(string? Text = null, long? DepartmentId = null, bool? Active = true);

public sealed record EventQuery(
    DateTime From,
    DateTime To,
    string? Text = null,
    long? EmployeeId = null,
    long? ReaderId = null,
    bool UnknownCardsOnly = false);

internal static class SearchText
{
    public static string[] Tokens(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.ToLower(CultureInfo.CurrentCulture).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public static string Key(Employee e)
        => string.Join(' ', new[] { e.LastName, e.FirstName, e.EmployeeNo, e.CardUid, e.Position }
            .Where(s => !string.IsNullOrWhiteSpace(s)))
            .ToLower(CultureInfo.CurrentCulture);

    /// <summary>Dokleja warunki LIKE dla każdego słowa – każde musi wystąpić w kluczu wyszukiwania.</summary>
    public static void Append(StringBuilder where, Dictionary<string, object?> args, string? text, string keyExpr, string? extraOrExpr = null)
    {
        var tokens = Tokens(text);
        for (int i = 0; i < tokens.Length; i++)
        {
            var p = "t" + i;
            where.Append($" AND ({keyExpr} LIKE @{p} ESCAPE '\\'");
            if (extraOrExpr != null) where.Append($" OR {extraOrExpr} LIKE @{p} ESCAPE '\\'");
            where.Append(')');
            args[p] = "%" + SqliteExtensions.LikeEscape(tokens[i]) + "%";
        }
    }
}

public sealed class DepartmentRepository(Database db)
{
    public List<Department> All()
    {
        using var c = db.Open();
        return c.Query("SELECT id, name FROM departments ORDER BY name", r => new Department { Id = r.Long("id"), Name = r.Str("name")! });
    }

    public long Save(Department d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) throw new RcpException("Nazwa działu nie może być pusta.");
        using var c = db.Open();
        try
        {
            if (d.Id == 0) return d.Id = c.InsertReturningId("INSERT INTO departments(name) VALUES(@Name)", new { Name = d.Name.Trim() });
            c.Execute("UPDATE departments SET name=@Name WHERE id=@Id", new { Name = d.Name.Trim(), d.Id });
            return d.Id;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new RcpException($"Dział „{d.Name}” już istnieje.");
        }
    }

    public void Delete(long id)
    {
        using var c = db.Open();
        c.Execute("DELETE FROM departments WHERE id=@id", new { id });
    }

    internal static long GetOrCreate(SqliteConnection c, SqliteTransaction tx, string name, Dictionary<string, long> cache)
    {
        name = name.Trim();
        if (cache.TryGetValue(name, out var id)) return id;
        var existing = c.Scalar("SELECT id FROM departments WHERE name=@name", new { name }, tx);
        id = existing != null ? Convert.ToInt64(existing) : c.InsertReturningId("INSERT INTO departments(name) VALUES(@name)", new { name }, tx);
        cache[name] = id;
        return id;
    }
}

public sealed class EmployeeRepository(Database db)
{
    private const string SelectSql = """
        SELECT e.id, e.employee_no, e.first_name, e.last_name, e.department_id, d.name AS department_name,
               e.position, e.card_uid, e.active, e.daily_norm_minutes, e.notes
        FROM employees e LEFT JOIN departments d ON d.id = e.department_id
        """;

    private static Employee Map(SqliteDataReader r) => new()
    {
        Id = r.Long("id"),
        EmployeeNo = r.Str("employee_no")!,
        FirstName = r.Str("first_name")!,
        LastName = r.Str("last_name")!,
        DepartmentId = r.LongN("department_id"),
        DepartmentName = r.Str("department_name"),
        Position = r.Str("position"),
        CardUid = r.Str("card_uid"),
        Active = r.Bool("active"),
        DailyNormMinutes = r.Int("daily_norm_minutes"),
        Notes = r.Str("notes"),
    };

    private static (string Where, Dictionary<string, object?> Args) BuildWhere(EmployeeQuery q)
    {
        var where = new StringBuilder("WHERE 1=1");
        var args = new Dictionary<string, object?>();
        if (q.DepartmentId is { } dep) { where.Append(" AND e.department_id = @dep"); args["dep"] = dep; }
        if (q.Active is { } act) { where.Append(" AND e.active = @act"); args["act"] = act; }
        SearchText.Append(where, args, q.Text, "e.search_key");
        return (where.ToString(), args);
    }

    public int Count(EmployeeQuery q)
    {
        var (where, args) = BuildWhere(q);
        using var c = db.Open();
        return Convert.ToInt32(c.Scalar($"SELECT COUNT(*) FROM employees e {where}", args));
    }

    public List<Employee> Search(EmployeeQuery q, int offset, int limit)
    {
        var (where, args) = BuildWhere(q);
        args["off"] = offset;
        args["lim"] = limit;
        using var c = db.Open();
        return c.Query($"{SelectSql} {where} ORDER BY e.last_name COLLATE NOCASE, e.first_name COLLATE NOCASE, e.id LIMIT @lim OFFSET @off", Map, args);
    }

    /// <summary>Wszyscy pasujący – strumieniowo (raporty, eksport).</summary>
    public IEnumerable<Employee> StreamAll(EmployeeQuery q)
    {
        var (where, args) = BuildWhere(q);
        using var c = db.Open();
        foreach (var e in c.Stream($"{SelectSql} {where} ORDER BY e.last_name COLLATE NOCASE, e.first_name COLLATE NOCASE, e.id", Map, args))
            yield return e;
    }

    public Employee? Get(long id)
    {
        using var c = db.Open();
        return c.Query($"{SelectSql} WHERE e.id=@id", Map, new { id }).FirstOrDefault();
    }

    public Employee? GetByCard(string cardUid)
    {
        if (string.IsNullOrEmpty(cardUid)) return null;
        using var c = db.Open();
        return c.Query($"{SelectSql} WHERE e.card_uid=@cardUid", Map, new { cardUid }).FirstOrDefault();
    }

    public long Save(Employee e)
    {
        Validate(e);
        using var c = db.Open();
        try
        {
            var args = Args(e);
            if (e.Id == 0)
            {
                e.Id = c.InsertReturningId("""
                    INSERT INTO employees(employee_no, first_name, last_name, department_id, position, card_uid,
                                          active, daily_norm_minutes, notes, search_key)
                    VALUES(@no, @fn, @ln, @dep, @pos, @card, @act, @norm, @notes, @key)
                    """, args);
            }
            else
            {
                args["id"] = e.Id;
                c.Execute("""
                    UPDATE employees SET employee_no=@no, first_name=@fn, last_name=@ln, department_id=@dep,
                        position=@pos, card_uid=@card, active=@act, daily_norm_minutes=@norm, notes=@notes, search_key=@key
                    WHERE id=@id
                    """, args);
            }
            return e.Id;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw Duplicate(c, e);
        }
    }

    private static Exception Duplicate(SqliteConnection c, Employee e)
    {
        if (!string.IsNullOrEmpty(e.CardUid))
        {
            var owner = c.Scalar("SELECT last_name || ' ' || first_name FROM employees WHERE card_uid=@card AND id<>@id",
                new { card = e.CardUid, id = e.Id });
            if (owner != null) return new RcpException($"Karta {e.CardUid} jest już przypisana do: {owner}.");
        }
        return new RcpException($"Pracownik o numerze „{e.EmployeeNo}” już istnieje.");
    }

    private static void Validate(Employee e)
    {
        e.EmployeeNo = e.EmployeeNo.Trim();
        e.FirstName = e.FirstName.Trim();
        e.LastName = e.LastName.Trim();
        e.CardUid = string.IsNullOrWhiteSpace(e.CardUid) ? null : CardUid.Normalize(e.CardUid);
        if (e.EmployeeNo.Length == 0) throw new RcpException("Numer ewidencyjny jest wymagany.");
        if (e.LastName.Length == 0) throw new RcpException("Nazwisko jest wymagane.");
        if (e.DailyNormMinutes is < 0 or > 24 * 60) throw new RcpException("Nieprawidłowa norma dobowa.");
    }

    private static Dictionary<string, object?> Args(Employee e) => new()
    {
        ["no"] = e.EmployeeNo, ["fn"] = e.FirstName, ["ln"] = e.LastName, ["dep"] = e.DepartmentId,
        ["pos"] = e.Position, ["card"] = e.CardUid, ["act"] = e.Active, ["norm"] = e.DailyNormMinutes,
        ["notes"] = e.Notes, ["key"] = SearchText.Key(e),
    };

    public void Delete(long id)
    {
        using var c = db.Open();
        c.Execute("DELETE FROM employees WHERE id=@id", new { id });
    }

    /// <summary>
    /// Import / aktualizacja masowa (dopasowanie po numerze ewidencyjnym). Jedna transakcja – dziesiątki tysięcy wierszy w sekundy.
    /// </summary>
    public (int Inserted, int Updated, List<string> Errors) Upsert(IEnumerable<(Employee Emp, string? Department)> rows)
    {
        int ins = 0, upd = 0;
        var errors = new List<string>();
        var deptCache = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        int line = 1;
        foreach (var (e, dept) in rows)
        {
            line++;
            try
            {
                Validate(e);
                if (!string.IsNullOrWhiteSpace(dept)) e.DepartmentId = DepartmentRepository.GetOrCreate(c, tx, dept, deptCache);
                var args = Args(e);
                var existing = c.Scalar("SELECT id FROM employees WHERE employee_no=@no", new { no = e.EmployeeNo }, tx);
                if (existing == null)
                {
                    c.Execute("""
                        INSERT INTO employees(employee_no, first_name, last_name, department_id, position, card_uid,
                                              active, daily_norm_minutes, notes, search_key)
                        VALUES(@no, @fn, @ln, @dep, @pos, @card, @act, @norm, @notes, @key)
                        """, args, tx);
                    ins++;
                }
                else
                {
                    args["id"] = Convert.ToInt64(existing);
                    c.Execute("""
                        UPDATE employees SET first_name=@fn, last_name=@ln, department_id=COALESCE(@dep, department_id),
                            position=COALESCE(@pos, position), card_uid=COALESCE(@card, card_uid), active=@act,
                            daily_norm_minutes=@norm, search_key=@key
                        WHERE id=@id
                        """, args, tx);
                    upd++;
                }
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                errors.Add($"Wiersz {line}: duplikat karty {e.CardUid} ({e.EmployeeNo}).");
            }
            catch (RcpException ex)
            {
                errors.Add($"Wiersz {line}: {ex.Message}");
            }
        }
        tx.Commit();
        return (ins, upd, errors);
    }

    public int TotalCount()
    {
        using var c = db.Open();
        return Convert.ToInt32(c.Scalar("SELECT COUNT(*) FROM employees"));
    }
}

public sealed class EventRepository(Database db)
{
    private const string SelectSql = """
        SELECT ev.id, ev.employee_id, ev.card_uid, ev.ts, ev.direction, ev.reader_id, ev.source, ev.note,
               emp.last_name || ' ' || emp.first_name AS employee_name, emp.employee_no, r.name AS reader_name,
               d.name AS department_name
        FROM events ev
        LEFT JOIN employees emp ON emp.id = ev.employee_id
        LEFT JOIN departments d ON d.id = emp.department_id
        LEFT JOIN readers r ON r.id = ev.reader_id
        """;

    private static AttendanceEvent Map(SqliteDataReader r) => new()
    {
        Id = r.Long("id"),
        EmployeeId = r.LongN("employee_id"),
        CardUid = r.Str("card_uid") ?? "",
        Timestamp = r.DateTimeV("ts"),
        Direction = (Direction)r.Int("direction"),
        ReaderId = r.LongN("reader_id"),
        Source = (EventSource)r.Int("source"),
        Note = r.Str("note"),
        EmployeeName = r.Str("employee_name"),
        EmployeeNo = r.Str("employee_no"),
        ReaderName = r.Str("reader_name"),
        DepartmentName = r.Str("department_name"),
    };

    public long Insert(AttendanceEvent e)
    {
        using var c = db.Open();
        return e.Id = c.InsertReturningId("""
            INSERT INTO events(employee_id, card_uid, ts, direction, reader_id, source, note)
            VALUES(@EmployeeId, @CardUid, @Timestamp, @Direction, @ReaderId, @Source, @Note)
            """, new { e.EmployeeId, e.CardUid, e.Timestamp, e.Direction, e.ReaderId, e.Source, e.Note });
    }

    public void Update(AttendanceEvent e)
    {
        using var c = db.Open();
        c.Execute("UPDATE events SET employee_id=@EmployeeId, ts=@Timestamp, direction=@Direction, note=@Note, source=@Source WHERE id=@Id",
            new { e.EmployeeId, e.Timestamp, e.Direction, e.Note, e.Source, e.Id });
    }

    public void Delete(long id)
    {
        using var c = db.Open();
        c.Execute("DELETE FROM events WHERE id=@id", new { id });
    }

    /// <summary>Przypisuje wcześniejsze odbicia nieznanej karty do pracownika (po zarejestrowaniu karty).</summary>
    public int AssignUnknownCard(string cardUid, long employeeId)
    {
        using var c = db.Open();
        return c.Execute("UPDATE events SET employee_id=@employeeId WHERE employee_id IS NULL AND card_uid=@cardUid", new { cardUid, employeeId });
    }

    private static (string Where, Dictionary<string, object?> Args) BuildWhere(EventQuery q)
    {
        var where = new StringBuilder("WHERE ev.ts >= @from AND ev.ts < @to");
        var args = new Dictionary<string, object?> { ["from"] = q.From, ["to"] = q.To };
        if (q.EmployeeId is { } emp) { where.Append(" AND ev.employee_id=@emp"); args["emp"] = emp; }
        if (q.ReaderId is { } rd) { where.Append(" AND ev.reader_id=@rd"); args["rd"] = rd; }
        if (q.UnknownCardsOnly) where.Append(" AND ev.employee_id IS NULL");

        var tokens = SearchText.Tokens(q.Text);
        if (tokens.Length > 0)
        {
            // najpierw pracownicy pasujący do tekstu (mała tabela), potem ich zdarzenia z indeksu (employee_id, ts)
            var empWhere = new StringBuilder("1=1");
            SearchText.Append(empWhere, args, q.Text, "s.search_key");
            where.Append($" AND (ev.employee_id IN (SELECT s.id FROM employees s WHERE {empWhere})");
            if (tokens.Length == 1 && tokens[0].Length >= 4 && CardUid.IsHex(tokens[0]))
            {
                where.Append(" OR ev.card_uid = @card");
                args["card"] = tokens[0].ToUpperInvariant();
            }
            where.Append(')');
        }
        return (where.ToString(), args);
    }

    public int Count(EventQuery q)
    {
        var (where, args) = BuildWhere(q);
        using var c = db.Open();
        return Convert.ToInt32(c.Scalar($"SELECT COUNT(*) FROM events ev {where}", args));
    }

    public List<AttendanceEvent> Search(EventQuery q, int offset, int limit)
    {
        var (where, args) = BuildWhere(q);
        args["off"] = offset;
        args["lim"] = limit;
        using var c = db.Open();
        return c.Query($"{SelectSql} {where} ORDER BY ev.ts DESC, ev.id DESC LIMIT @lim OFFSET @off", Map, args);
    }

    public IEnumerable<AttendanceEvent> StreamAll(EventQuery q)
    {
        var (where, args) = BuildWhere(q);
        using var c = db.Open();
        foreach (var e in c.Stream($"{SelectSql} {where} ORDER BY ev.ts, ev.id", Map, args)) yield return e;
    }

    public List<AttendanceEvent> ForEmployee(long employeeId, DateTime from, DateTime to)
    {
        using var c = db.Open();
        return c.Query($"{SelectSql} WHERE ev.employee_id=@employeeId AND ev.ts >= @from AND ev.ts < @to ORDER BY ev.ts, ev.id",
            Map, new { employeeId, from, to });
    }

    public AttendanceEvent? LastForEmployee(long employeeId, DateTime since)
    {
        using var c = db.Open();
        return c.Query($"{SelectSql} WHERE ev.employee_id=@employeeId AND ev.ts >= @since ORDER BY ev.ts DESC, ev.id DESC LIMIT 1",
            Map, new { employeeId, since }).FirstOrDefault();
    }

    public List<AttendanceEvent> Recent(int limit)
    {
        using var c = db.Open();
        return c.Query($"{SelectSql} ORDER BY ev.id DESC LIMIT @limit", Map, new { limit });
    }

    /// <summary>Kto jest teraz w pracy: ostatnie zdarzenie (w granicach zmiany) to wejście.</summary>
    public List<AttendanceEvent> CurrentlyPresent(DateTime since)
    {
        using var c = db.Open();
        return c.Query($"""
            {SelectSql}
            JOIN (SELECT employee_id, MAX(ts) AS mts FROM events
                  WHERE ts >= @since AND employee_id IS NOT NULL GROUP BY employee_id) last
              ON last.employee_id = ev.employee_id AND last.mts = ev.ts
            WHERE ev.direction = 1
            ORDER BY emp.last_name COLLATE NOCASE, emp.first_name COLLATE NOCASE
            """, Map, new { since });
    }

    /// <summary>Wszystkie zdarzenia z zakresu pogrupowane po pracowniku (do raportów zbiorczych).</summary>
    public Dictionary<long, List<(DateTime, Direction)>> ByEmployee(DateTime from, DateTime to, long? departmentId)
    {
        var result = new Dictionary<long, List<(DateTime, Direction)>>();
        using var c = db.Open();
        // bez JOIN i ORDER BY – skan indeksu po dacie; sortowanie w pamięci przy parowaniu
        var sql = departmentId == null
            ? "SELECT employee_id, ts, direction FROM events WHERE ts >= @from AND ts < @to AND employee_id IS NOT NULL"
            : """
              SELECT employee_id, ts, direction FROM events
              WHERE ts >= @from AND ts < @to AND employee_id IN (SELECT id FROM employees WHERE department_id = @dep)
              """;
        using var cmd = SqliteExtensions.Create(c, sql, new { from, to, dep = departmentId }, null);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetInt64(0);
            if (!result.TryGetValue(id, out var list)) result[id] = list = new List<(DateTime, Direction)>();
            list.Add((SqliteExtensions.ParseDateTime(r.GetString(1)), (Direction)r.GetInt32(2)));
        }
        return result;
    }
}

public sealed class AbsenceRepository(Database db)
{
    private static Absence Map(SqliteDataReader r) => new()
    {
        Id = r.Long("id"),
        EmployeeId = r.Long("employee_id"),
        DateFrom = r.DateV("date_from"),
        DateTo = r.DateV("date_to"),
        Type = (AbsenceType)r.Int("type"),
        Note = r.Str("note"),
    };

    public List<Absence> ForEmployee(long employeeId, DateOnly from, DateOnly to)
    {
        using var c = db.Open();
        return c.Query("SELECT * FROM absences WHERE employee_id=@employeeId AND date_from <= @to AND date_to >= @from ORDER BY date_from",
            Map, new { employeeId, from, to });
    }

    public Dictionary<long, List<Absence>> ByEmployee(DateOnly from, DateOnly to)
    {
        using var c = db.Open();
        return c.Query("SELECT * FROM absences WHERE date_from <= @to AND date_to >= @from", Map, new { from, to })
            .GroupBy(a => a.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
    }

    public long Save(Absence a)
    {
        if (a.DateTo < a.DateFrom) throw new RcpException("Data końcowa nie może być wcześniejsza niż początkowa.");
        using var c = db.Open();
        var overlap = c.Scalar("SELECT COUNT(*) FROM absences WHERE employee_id=@EmployeeId AND id<>@Id AND date_from <= @DateTo AND date_to >= @DateFrom",
            new { a.EmployeeId, a.Id, a.DateFrom, a.DateTo });
        if (Convert.ToInt32(overlap) > 0) throw new RcpException("W tym okresie jest już zarejestrowana nieobecność.");
        var args = new { a.EmployeeId, a.DateFrom, a.DateTo, a.Type, a.Note, a.Id };
        if (a.Id == 0)
            return a.Id = c.InsertReturningId("INSERT INTO absences(employee_id, date_from, date_to, type, note) VALUES(@EmployeeId, @DateFrom, @DateTo, @Type, @Note)", args);
        c.Execute("UPDATE absences SET date_from=@DateFrom, date_to=@DateTo, type=@Type, note=@Note WHERE id=@Id", args);
        return a.Id;
    }

    public void Delete(long id)
    {
        using var c = db.Open();
        c.Execute("DELETE FROM absences WHERE id=@id", new { id });
    }
}

public sealed class ReaderRepository(Database db)
{
    public List<ReaderConfig> All()
    {
        using var c = db.Open();
        return c.Query("SELECT * FROM readers ORDER BY name", r => new ReaderConfig
        {
            Id = r.Long("id"),
            Name = r.Str("name")!,
            Kind = (ReaderKind)r.Int("kind"),
            Mode = (ReaderMode)r.Int("mode"),
            DeviceId = r.Str("device_id") ?? "",
            BaudRate = r.Int("baud_rate"),
            DecimalToHex = r.Bool("decimal_to_hex"),
            ReverseBytes = r.Bool("reverse_bytes"),
            Enabled = r.Bool("enabled"),
        });
    }

    public long Save(ReaderConfig rc)
    {
        if (string.IsNullOrWhiteSpace(rc.Name)) throw new RcpException("Podaj nazwę czytnika.");
        using var c = db.Open();
        var args = new { rc.Name, rc.Kind, rc.Mode, rc.DeviceId, rc.BaudRate, rc.DecimalToHex, rc.ReverseBytes, rc.Enabled, rc.Id };
        if (rc.Id == 0)
            return rc.Id = c.InsertReturningId("""
                INSERT INTO readers(name, kind, mode, device_id, baud_rate, decimal_to_hex, reverse_bytes, enabled)
                VALUES(@Name, @Kind, @Mode, @DeviceId, @BaudRate, @DecimalToHex, @ReverseBytes, @Enabled)
                """, args);
        c.Execute("""
            UPDATE readers SET name=@Name, kind=@Kind, mode=@Mode, device_id=@DeviceId, baud_rate=@BaudRate,
                decimal_to_hex=@DecimalToHex, reverse_bytes=@ReverseBytes, enabled=@Enabled WHERE id=@Id
            """, args);
        return rc.Id;
    }

    public void Delete(long id)
    {
        using var c = db.Open();
        c.Execute("DELETE FROM readers WHERE id=@id", new { id });
    }
}

public sealed class CalendarRepository(Database db)
{
    public List<CompanyDayOff> All()
    {
        using var c = db.Open();
        return c.Query("SELECT date, name FROM company_days_off ORDER BY date", r => new CompanyDayOff { Date = r.DateV("date"), Name = r.Str("name")! });
    }

    public void Save(CompanyDayOff d)
    {
        using var c = db.Open();
        c.Execute("INSERT INTO company_days_off(date, name) VALUES(@Date, @Name) ON CONFLICT(date) DO UPDATE SET name=excluded.name",
            new { d.Date, d.Name });
    }

    public void Delete(DateOnly date)
    {
        using var c = db.Open();
        c.Execute("DELETE FROM company_days_off WHERE date=@date", new { date });
    }

    public WorkCalendar Load() => new(All());
}

public sealed class SettingsRepository(Database db)
{
    public AppSettings Load()
    {
        using var c = db.Open();
        var values = c.Query("SELECT key, value FROM settings", r => (r.Str("key")!, r.Str("value")))
            .ToDictionary(x => x.Item1, x => x.Item2);
        var s = new AppSettings();
        if (values.TryGetValue("debounce_seconds", out var v) && int.TryParse(v, out var i)) s.DebounceSeconds = i;
        if (values.TryGetValue("max_shift_hours", out v) && int.TryParse(v, out i)) s.MaxShiftHours = i;
        if (values.TryGetValue("company_name", out v)) s.CompanyName = v ?? "";
        return s;
    }

    public void Save(AppSettings s)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        void Set(string key, string value) =>
            c.Execute("INSERT INTO settings(key, value) VALUES(@key, @value) ON CONFLICT(key) DO UPDATE SET value=excluded.value", new { key, value }, tx);
        Set("debounce_seconds", s.DebounceSeconds.ToString(CultureInfo.InvariantCulture));
        Set("max_shift_hours", s.MaxShiftHours.ToString(CultureInfo.InvariantCulture));
        Set("company_name", s.CompanyName);
        tx.Commit();
    }
}

public sealed class AuditRepository(Database db)
{
    public void Log(string action, string? details = null)
    {
        try
        {
            using var c = db.Open();
            c.Execute("INSERT INTO audit_log(user, action, details) VALUES(@user, @action, @details)",
                new { user = Environment.UserName, action, details });
        }
        catch
        {
            // dziennik audytu nie może blokować pracy
        }
    }
}
