using System.Globalization;
using System.Text;

namespace Rcp.Core.Services;

/// <summary>CSV w formacie zgodnym z polskim Excelem (średnik, UTF-8 z BOM).</summary>
public static class Csv
{
    public const char Separator = ';';

    public static string Escape(object? v)
    {
        var s = v switch
        {
            null => "",
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.CurrentCulture),
            _ => v.ToString() ?? "",
        };
        return s.IndexOfAny(new[] { Separator, '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    public static void Write<T>(string path, IEnumerable<string> headers, IEnumerable<T> rows, Func<T, IEnumerable<object?>> cells)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(true));
        w.WriteLine(string.Join(Separator, headers.Select(Escape)));
        foreach (var row in rows) w.WriteLine(string.Join(Separator, cells(row).Select(Escape)));
    }

    public static IEnumerable<string[]> Read(string path)
    {
        using var r = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var firstLine = r.ReadLine();
        if (firstLine == null) yield break;
        char sep = firstLine.Count(c => c == ';') >= firstLine.Count(c => c == ',') ? ';' : ',';
        yield return ParseLine(firstLine, sep);
        string? line;
        while ((line = r.ReadLine()) != null)
        {
            // pola w cudzysłowie mogą zawierać znak nowej linii
            while (line.Count(c => c == '"') % 2 == 1 && r.ReadLine() is { } next) line += "\n" + next;
            if (line.Length > 0) yield return ParseLine(line, sep);
        }
    }

    public static string[] ParseLine(string line, char sep)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else sb.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == sep) { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        result.Add(sb.ToString());
        return result.ToArray();
    }
}

/// <summary>Import pracowników z CSV. Nagłówki rozpoznawane po nazwie (kolejność dowolna).</summary>
public static class EmployeeCsv
{
    public static readonly string[] Headers = { "nr", "nazwisko", "imie", "dzial", "stanowisko", "karta", "norma_h", "aktywny" };

    public static IEnumerable<(Employee, string?)> Parse(string path)
    {
        using var it = Csv.Read(path).GetEnumerator();
        if (!it.MoveNext()) yield break;
        var header = it.Current.Select(Normalize).ToArray();
        int Col(params string[] names) => Array.FindIndex(header, h => names.Contains(h));
        int cNo = Col("nr", "numer", "id", "nrewidencyjny"), cLn = Col("nazwisko"), cFn = Col("imie"),
            cDep = Col("dzial", "departament"), cPos = Col("stanowisko"), cCard = Col("karta", "uid", "nrkarty"),
            cNorm = Col("normah", "norma"), cAct = Col("aktywny");
        if (cNo < 0 || cLn < 0) throw new Data.RcpException("Plik musi zawierać kolumny „nr” i „nazwisko”.");

        while (it.MoveNext())
        {
            var row = it.Current;
            string? Get(int i) => i >= 0 && i < row.Length && !string.IsNullOrWhiteSpace(row[i]) ? row[i].Trim() : null;
            var emp = new Employee
            {
                EmployeeNo = Get(cNo) ?? "",
                LastName = Get(cLn) ?? "",
                FirstName = Get(cFn) ?? "",
                Position = Get(cPos),
                CardUid = Get(cCard),
                Active = Get(cAct) is not { } a || a is not ("0" or "nie" or "false" or "n"),
            };
            if (Get(cNorm) is { } norm && double.TryParse(norm.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
                emp.DailyNormMinutes = (int)Math.Round(h * 60);
            yield return (emp, Get(cDep));
        }
    }

    private static string Normalize(string h)
    {
        var s = h.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace(".", "");
        return s.Replace('ą', 'a').Replace('ć', 'c').Replace('ę', 'e').Replace('ł', 'l').Replace('ń', 'n')
                .Replace('ó', 'o').Replace('ś', 's').Replace('ź', 'z').Replace('ż', 'z');
    }
}
