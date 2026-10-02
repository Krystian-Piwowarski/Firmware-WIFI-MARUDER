using Rcp.Core;

namespace Rcp.App.Readers;

/// <summary>Czytnik USB w trybie klawiatury (HID keyboard wedge), identyfikowany ścieżką urządzenia.</summary>
public sealed class KeyboardReaderDriver : ReaderDriver
{
    private readonly RawInputKeyboardHub _hub;

    public KeyboardReaderDriver(ReaderConfig config, RawInputKeyboardHub hub) : base(config)
    {
        _hub = hub;
    }

    public override void Start()
    {
        if (string.IsNullOrWhiteSpace(Config.DeviceId))
        {
            SetStatus("Nie wybrano urządzenia – użyj „Wykryj” w konfiguracji", false);
            return;
        }
        _hub.Scan += OnScan;
        SetStatus("Nasłuch (USB klawiatura)", true);
    }

    private void OnScan(string device, string text)
    {
        if (string.Equals(device, Config.DeviceId, StringComparison.OrdinalIgnoreCase)) RaiseCard(text);
    }

    public override void Dispose() => _hub.Scan -= OnScan;
}
