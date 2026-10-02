using System.Globalization;
using System.Text;

namespace Rcp.Core;

/// <summary>Normalizacja numerów kart Mifare do jednej postaci (HEX, wielkie litery, bez separatorów).</summary>
public static class CardUid
{
    public static string Normalize(string raw, bool decimalToHex = false, bool reverseBytes = false)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw.Trim())
        {
            if (ch is ' ' or ':' or '-' or '\t') continue;
            sb.Append(char.ToUpperInvariant(ch));
        }
        var s = sb.ToString();

        if (decimalToHex && s.Length > 0 && s.All(char.IsAsciiDigit)
            && ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            s = value.ToString("X", CultureInfo.InvariantCulture);
            if (s.Length % 2 == 1) s = "0" + s;
        }

        if (reverseBytes && s.Length % 2 == 0 && IsHex(s))
        {
            var bytes = new StringBuilder(s.Length);
            for (int i = s.Length - 2; i >= 0; i -= 2) bytes.Append(s, i, 2);
            s = bytes.ToString();
        }

        return s;
    }

    public static bool IsHex(string s) => s.Length > 0 && s.All(char.IsAsciiHexDigit);

    public static string FromBytes(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);
}

/// <summary>
/// Wyciąga numery kart ze strumienia tekstowego (port COM, TCP).
/// Obsługuje zakończenia CR/LF, ramki STX/ETX oraz czytniki bez terminatora (flush po bezczynności).
/// </summary>
public sealed class LineCardParser
{
    private readonly StringBuilder _buffer = new();
    private DateTime _lastChar = DateTime.MinValue;

    public TimeSpan IdleFlush { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Podaje kolejny znak; zwraca numer karty, jeśli zakończono ramkę.</summary>
    public string? Feed(char ch, DateTime now)
    {
        _lastChar = now;
        if (ch is '\r' or '\n' or (char)0x03)
            return Flush();
        if (ch == (char)0x02) { _buffer.Clear(); return null; }
        if (!char.IsControl(ch)) _buffer.Append(ch);
        if (_buffer.Length > 256) _buffer.Clear();
        return null;
    }

    /// <summary>Wywoływane okresowo – oddaje bufor, gdy czytnik nie wysyła terminatora.</summary>
    public string? FlushIfIdle(DateTime now)
        => _buffer.Length > 0 && now - _lastChar >= IdleFlush ? Flush() : null;

    public string? Flush()
    {
        var line = _buffer.ToString();
        _buffer.Clear();
        return ExtractUid(line);
    }

    public static string? ExtractUid(string line)
    {
        line = line.Trim();
        if (line.Length == 0) return null;

        // cała linia to numer (np. "04A23B11", "04:A2:3B:11", "1234567890")
        if (line.All(c => char.IsAsciiHexDigit(c) || c is ' ' or ':' or '-'))
        {
            var compact = CardUid.Normalize(line);
            return compact.Length >= 4 ? compact : null;
        }

        // linia z opisem, np. "UID: 04A23B11" lub "CARD=1234567" – bierzemy ostatni token HEX
        var tokens = line.Split(new[] { ' ', ':', '=', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = tokens.Length - 1; i >= 0; i--)
        {
            var t = tokens[i];
            if (t.Length >= 4 && CardUid.IsHex(t)) return t.ToUpperInvariant();
        }
        return null;
    }
}
