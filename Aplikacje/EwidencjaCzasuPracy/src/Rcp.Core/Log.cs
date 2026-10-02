namespace Rcp.Core;

/// <summary>Prosty dziennik plikowy (jeden plik na dzień).</summary>
public static class Log
{
    private static readonly object Lock = new();
    public static string Directory { get; set; } = Path.Combine(Path.GetTempPath(), "EwidencjaRCP", "logs");

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message, Exception? ex = null) => Write("BŁĄD", ex == null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(Path.Combine(Directory, $"rcp-{DateTime.Now:yyyy-MM-dd}.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // logowanie nie może przerwać działania aplikacji
        }
    }
}
