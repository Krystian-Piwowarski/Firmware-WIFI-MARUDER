using Rcp.Core;

namespace Rcp.App.Readers;

/// <summary>Bazowa klasa sterownika czytnika kart.</summary>
public abstract class ReaderDriver : IDisposable
{
    protected ReaderDriver(ReaderConfig config) => Config = config;

    public ReaderConfig Config { get; }
    public string Status { get; private set; } = "Uruchamianie…";
    public bool IsOk { get; private set; }

    /// <summary>Surowy numer karty (przed normalizacją). Może być wywołane z wątku tła.</summary>
    public event Action<ReaderDriver, string>? CardRead;
    public event Action<ReaderDriver>? StatusChanged;

    public abstract void Start();

    protected void SetStatus(string status, bool ok)
    {
        if (status == Status && ok == IsOk) return;
        Status = status;
        IsOk = ok;
        if (!ok) Log.Info($"Czytnik „{Config.Name}”: {status}");
        StatusChanged?.Invoke(this);
    }

    protected void RaiseCard(string uid) => CardRead?.Invoke(this, uid);

    public virtual void Dispose() { }

    public static ReaderDriver Create(ReaderConfig config, Func<RawInputKeyboardHub> keyboardHub) => config.Kind switch
    {
        ReaderKind.Keyboard => new KeyboardReaderDriver(config, keyboardHub()),
        ReaderKind.Serial => new SerialReaderDriver(config),
        ReaderKind.Tcp => new TcpReaderDriver(config),
        ReaderKind.PcSc => new PcscReaderDriver(config),
        _ => throw new NotSupportedException(config.Kind.ToString()),
    };
}

/// <summary>Sterownik działający we własnym wątku tła z automatycznym ponawianiem połączenia.</summary>
public abstract class ThreadedReaderDriver(ReaderConfig config) : ReaderDriver(config)
{
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;

    public override void Start()
    {
        _thread = new Thread(() =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    Run(_cts.Token);
                }
                catch (Exception ex) when (!_cts.IsCancellationRequested)
                {
                    SetStatus("Błąd: " + ex.Message, false);
                }
                // ponowna próba po przerwie (odłączony kabel, restart czytnika itp.)
                _cts.Token.WaitHandle.WaitOne(3000);
            }
        })
        { IsBackground = true, Name = "Czytnik " + Config.Name };
        _thread.Start();
    }

    /// <summary>Pętla odczytu; powinna wracać po rozłączeniu lub anulowaniu.</summary>
    protected abstract void Run(CancellationToken ct);

    protected virtual void OnStopping() { }

    public override void Dispose()
    {
        _cts.Cancel();
        try { OnStopping(); } catch { /* ignoruj */ }
        _thread?.Join(3000);
        _cts.Dispose();
    }
}
