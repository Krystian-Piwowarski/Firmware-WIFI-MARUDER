using Rcp.Core;

namespace Rcp.App.Readers;

/// <summary>Uruchamia sterowniki skonfigurowanych czytników i przekazuje odczyty do wątku UI.</summary>
public sealed class ReaderManager : IDisposable
{
    private readonly SynchronizationContext _ui;
    private readonly List<ReaderDriver> _drivers = new();
    private RawInputKeyboardHub? _hub;

    public ReaderManager()
    {
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("ReaderManager musi powstać w wątku UI");
    }

    public event Action<ReaderConfig, string>? CardRead;
    public event Action? StatusChanged;

    public IReadOnlyList<ReaderDriver> Drivers => _drivers;

    /// <summary>Wspólny nasłuch Raw Input (tworzony przy pierwszym użyciu, w wątku UI).</summary>
    public RawInputKeyboardHub KeyboardHub => _hub ??= new RawInputKeyboardHub();

    public void Load(IEnumerable<ReaderConfig> configs)
    {
        StopAll();
        var list = configs.Where(c => c.Enabled).ToList();
        foreach (var config in list)
        {
            ReaderDriver driver;
            try
            {
                driver = ReaderDriver.Create(config, () => KeyboardHub);
            }
            catch (Exception ex)
            {
                Log.Error($"Nie można utworzyć czytnika {config.Name}", ex);
                continue;
            }
            driver.CardRead += (d, uid) => _ui.Post(_ =>
            {
                Log.Info($"Odczyt karty {uid} na czytniku „{d.Config.Name}”");
                CardRead?.Invoke(d.Config, uid);
            }, null);
            driver.StatusChanged += _ => _ui.Post(_ => StatusChanged?.Invoke(), null);
            _drivers.Add(driver);
            try { driver.Start(); }
            catch (Exception ex) { Log.Error($"Start czytnika {config.Name}", ex); }
        }
        _hub?.SetReaderDevices(list.Where(c => c.Kind == ReaderKind.Keyboard).Select(c => c.DeviceId));
        StatusChanged?.Invoke();
    }

    private void StopAll()
    {
        foreach (var d in _drivers)
        {
            try { d.Dispose(); } catch (Exception ex) { Log.Error("Zatrzymanie czytnika", ex); }
        }
        _drivers.Clear();
    }

    public void Dispose()
    {
        StopAll();
        _hub?.Dispose();
        _hub = null;
    }
}
