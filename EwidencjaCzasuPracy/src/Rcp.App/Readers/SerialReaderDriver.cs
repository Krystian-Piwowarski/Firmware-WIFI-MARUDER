using System.IO.Ports;
using Rcp.Core;

namespace Rcp.App.Readers;

/// <summary>Czytnik na porcie COM (RS232, RS485 przez konwerter, USB-CDC). Odbiera numer karty jako tekst.</summary>
public sealed class SerialReaderDriver(ReaderConfig config) : ThreadedReaderDriver(config)
{
    private SerialPort? _port;

    protected override void Run(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Config.DeviceId))
        {
            SetStatus("Nie wybrano portu COM", false);
            ct.WaitHandle.WaitOne(Timeout.Infinite);
            return;
        }

        using var port = new SerialPort(Config.DeviceId, Config.BaudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 100,
            DtrEnable = true,
            RtsEnable = true,
        };
        port.Open();
        _port = port;
        SetStatus($"Połączono {Config.DeviceId} @ {Config.BaudRate}", true);

        var parser = new LineCardParser();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                int b = port.ReadByte();
                if (b < 0) break;
                if (parser.Feed((char)b, DateTime.Now) is { } uid) RaiseCard(uid);
            }
            catch (TimeoutException)
            {
                if (parser.FlushIfIdle(DateTime.Now) is { } uid) RaiseCard(uid);
            }
        }
        _port = null;
    }

    protected override void OnStopping() => _port?.Close();
}
