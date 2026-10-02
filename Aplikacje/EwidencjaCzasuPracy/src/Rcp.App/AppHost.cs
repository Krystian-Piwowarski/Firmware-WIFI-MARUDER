using Rcp.App.Readers;
using Rcp.Core;
using Rcp.Core.Services;

namespace Rcp.App;

/// <summary>Wspólny stan aplikacji: baza, czytniki, przetwarzanie odbić.</summary>
public sealed class AppHost : IDisposable
{
    public AppHost(RcpContext ctx, AppConfig config)
    {
        Ctx = ctx;
        Config = config;
        Scanner = new ScanProcessor(ctx);
        Reports = new ReportService(ctx);
        Readers = new ReaderManager();
        Readers.CardRead += OnCardRead;
    }

    public RcpContext Ctx { get; }
    public AppConfig Config { get; }
    public ScanProcessor Scanner { get; }
    public ReportService Reports { get; }
    public ReaderManager Readers { get; }

    /// <summary>Odbicie karty zarejestrowane w bazie.</summary>
    public event Action<ScanResult>? Scanned;

    /// <summary>Zmiana danych (pracownicy, zdarzenia) – widoki mogą się odświeżyć.</summary>
    public event Action? DataChanged;

    /// <summary>
    /// Gdy ustawione, odczyt karty trafia tutaj zamiast do rejestracji (np. przypisywanie karty pracownikowi).
    /// Funkcja dostaje znormalizowany UID.
    /// </summary>
    public Action<string>? CardCapture { get; set; }

    public void ReloadReaders() => Readers.Load(Ctx.Readers.All());

    public void NotifyDataChanged() => DataChanged?.Invoke();

    private void OnCardRead(ReaderConfig reader, string raw)
    {
        if (CardCapture is { } capture)
        {
            capture(CardUid.Normalize(raw, reader.DecimalToHex, reader.ReverseBytes));
            return;
        }
        try
        {
            var result = Scanner.Process(reader, raw, DateTime.Now);
            if (result.Status != ScanStatus.Ignored) Log.Info(result.Message);
            Scanned?.Invoke(result);
        }
        catch (Exception ex)
        {
            Log.Error($"Rejestracja odbicia {raw}", ex);
        }
    }

    public void Dispose() => Readers.Dispose();
}
