using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Rcp.Core.Data;

/// <summary>Baza SQLite (tryb WAL) – jeden plik, bez serwera, szybka także przy setkach tysięcy pracowników.</summary>
public sealed class Database
{
    public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
    public const string DateFormat = "yyyy-MM-dd";
    private const int SchemaVersion = 1;

    private readonly string _connectionString;
    public string FilePath { get; }

    public Database(string filePath)
    {
        FilePath = filePath;
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            DefaultTimeout = 30,
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        c.Execute("PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 10000;");
        return c;
    }

    public void Initialize()
    {
        using var c = Open();
        c.Execute("PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;");
        var version = Convert.ToInt32(c.Scalar("PRAGMA user_version"));
        if (version >= SchemaVersion) return;

        using var tx = c.BeginTransaction();
        if (version < 1) c.Execute(Schema.V1, tx: tx);
        c.Execute($"PRAGMA user_version = {SchemaVersion}", tx: tx);
        tx.Commit();
    }

    /// <summary>Kopia zapasowa „na gorąco” (SQLite Online Backup API).</summary>
    public void Backup(string destinationFile)
    {
        using var src = Open();
        using var dst = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destinationFile }.ToString());
        dst.Open();
        src.BackupDatabase(dst);
    }

    private static class Schema
    {
        public const string V1 = """
            CREATE TABLE departments (
                id    INTEGER PRIMARY KEY,
                name  TEXT NOT NULL UNIQUE COLLATE NOCASE
            );

            CREATE TABLE employees (
                id                 INTEGER PRIMARY KEY,
                employee_no        TEXT NOT NULL UNIQUE COLLATE NOCASE,
                first_name         TEXT NOT NULL,
                last_name          TEXT NOT NULL,
                department_id      INTEGER REFERENCES departments(id) ON DELETE SET NULL,
                position           TEXT,
                card_uid           TEXT UNIQUE,
                active             INTEGER NOT NULL DEFAULT 1,
                daily_norm_minutes INTEGER NOT NULL DEFAULT 480,
                notes              TEXT,
                search_key         TEXT NOT NULL DEFAULT '',
                created_at         TEXT NOT NULL DEFAULT (datetime('now','localtime'))
            );
            CREATE INDEX ix_employees_name ON employees(last_name COLLATE NOCASE, first_name COLLATE NOCASE);
            CREATE INDEX ix_employees_dept ON employees(department_id, active);

            CREATE TABLE readers (
                id             INTEGER PRIMARY KEY,
                name           TEXT NOT NULL,
                kind           INTEGER NOT NULL,
                mode           INTEGER NOT NULL,
                device_id      TEXT NOT NULL DEFAULT '',
                baud_rate      INTEGER NOT NULL DEFAULT 9600,
                decimal_to_hex INTEGER NOT NULL DEFAULT 0,
                reverse_bytes  INTEGER NOT NULL DEFAULT 0,
                enabled        INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE events (
                id          INTEGER PRIMARY KEY,
                employee_id INTEGER REFERENCES employees(id) ON DELETE CASCADE,
                card_uid    TEXT NOT NULL DEFAULT '',
                ts          TEXT NOT NULL,
                direction   INTEGER NOT NULL,
                reader_id   INTEGER REFERENCES readers(id) ON DELETE SET NULL,
                source      INTEGER NOT NULL DEFAULT 1,
                note        TEXT
            );
            CREATE INDEX ix_events_emp_ts ON events(employee_id, ts);
            CREATE INDEX ix_events_ts ON events(ts, employee_id, direction); -- indeks pokrywający dla raportów
            CREATE INDEX ix_events_card_ts ON events(card_uid, ts);

            CREATE TABLE absences (
                id          INTEGER PRIMARY KEY,
                employee_id INTEGER NOT NULL REFERENCES employees(id) ON DELETE CASCADE,
                date_from   TEXT NOT NULL,
                date_to     TEXT NOT NULL,
                type        INTEGER NOT NULL,
                note        TEXT
            );
            CREATE INDEX ix_absences_emp ON absences(employee_id, date_from, date_to);

            CREATE TABLE company_days_off (
                date  TEXT PRIMARY KEY,
                name  TEXT NOT NULL
            );

            CREATE TABLE settings (
                key   TEXT PRIMARY KEY,
                value TEXT
            );

            CREATE TABLE audit_log (
                id      INTEGER PRIMARY KEY,
                ts      TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                user    TEXT,
                action  TEXT NOT NULL,
                details TEXT
            );
            """;
    }
}

/// <summary>Lekkie rozszerzenia ADO.NET – parametry z obiektów anonimowych, mapowanie wierszy.</summary>
public static class SqliteExtensions
{
    public static int Execute(this SqliteConnection c, string sql, object? args = null, SqliteTransaction? tx = null)
    {
        using var cmd = Create(c, sql, args, tx);
        return cmd.ExecuteNonQuery();
    }

    public static object? Scalar(this SqliteConnection c, string sql, object? args = null, SqliteTransaction? tx = null)
    {
        using var cmd = Create(c, sql, args, tx);
        var r = cmd.ExecuteScalar();
        return r is DBNull ? null : r;
    }

    public static long InsertReturningId(this SqliteConnection c, string sql, object? args = null, SqliteTransaction? tx = null)
    {
        using var cmd = Create(c, sql + "; SELECT last_insert_rowid();", args, tx);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public static List<T> Query<T>(this SqliteConnection c, string sql, Func<SqliteDataReader, T> map, object? args = null, SqliteTransaction? tx = null)
    {
        using var cmd = Create(c, sql, args, tx);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    public static IEnumerable<T> Stream<T>(this SqliteConnection c, string sql, Func<SqliteDataReader, T> map, object? args = null)
    {
        using var cmd = Create(c, sql, args, null);
        using var r = cmd.ExecuteReader();
        while (r.Read()) yield return map(r);
    }

    public static SqliteCommand Create(SqliteConnection c, string sql, object? args, SqliteTransaction? tx)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        if (args is IEnumerable<KeyValuePair<string, object?>> dict)
        {
            foreach (var (k, v) in dict) cmd.Parameters.AddWithValue("@" + k, ToDb(v));
        }
        else if (args != null)
        {
            foreach (var p in args.GetType().GetProperties())
                cmd.Parameters.AddWithValue("@" + p.Name, ToDb(p.GetValue(args)));
        }
        return cmd;
    }

    public static object ToDb(object? v) => v switch
    {
        null => DBNull.Value,
        DateTime dt => dt.ToString(Database.DateTimeFormat, CultureInfo.InvariantCulture),
        DateOnly d => d.ToString(Database.DateFormat, CultureInfo.InvariantCulture),
        bool b => b ? 1 : 0,
        Enum e => Convert.ToInt32(e),
        _ => v,
    };

    public static string? Str(this SqliteDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }

    public static long? LongN(this SqliteDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetInt64(i);
    }

    public static long Long(this SqliteDataReader r, string col) => r.GetInt64(r.GetOrdinal(col));
    public static int Int(this SqliteDataReader r, string col) => r.GetInt32(r.GetOrdinal(col));
    public static bool Bool(this SqliteDataReader r, string col) => r.GetInt64(r.GetOrdinal(col)) != 0;

    public static DateTime DateTimeV(this SqliteDataReader r, string col) => ParseDateTime(r.GetString(r.GetOrdinal(col)));

    /// <summary>Szybkie parsowanie „yyyy-MM-dd HH:mm:ss” (miliony wierszy w raportach).</summary>
    public static DateTime ParseDateTime(string s)
    {
        if (s.Length == 19 && s[4] == '-' && s[7] == '-' && s[10] == ' ' && s[13] == ':' && s[16] == ':')
        {
            static int D(string s, int i, int n)
            {
                int v = 0;
                for (int k = i; k < i + n; k++)
                {
                    int d = s[k] - '0';
                    if ((uint)d > 9) return -1;
                    v = v * 10 + d;
                }
                return v;
            }
            int y = D(s, 0, 4), mo = D(s, 5, 2), d = D(s, 8, 2), h = D(s, 11, 2), mi = D(s, 14, 2), se = D(s, 17, 2);
            if (y > 0 && mo > 0 && d > 0 && h >= 0 && mi >= 0 && se >= 0) return new DateTime(y, mo, d, h, mi, se);
        }
        return DateTime.ParseExact(s, Database.DateTimeFormat, CultureInfo.InvariantCulture);
    }

    public static DateOnly DateV(this SqliteDataReader r, string col)
        => DateOnly.ParseExact(r.GetString(r.GetOrdinal(col)), Database.DateFormat, CultureInfo.InvariantCulture);

    /// <summary>Escapowanie znaków specjalnych LIKE (używane z ESCAPE '\').</summary>
    public static string LikeEscape(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
