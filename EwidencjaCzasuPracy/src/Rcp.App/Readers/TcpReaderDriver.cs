using System.Net.Sockets;
using System.Text;
using Rcp.Core;

namespace Rcp.App.Readers;

/// <summary>Czytnik sieciowy (TCP klient) – np. czytnik z modułem Ethernet lub konwerter RS485↔TCP.</summary>
public sealed class TcpReaderDriver(ReaderConfig config) : ThreadedReaderDriver(config)
{
    private Socket? _socket;

    protected override void Run(CancellationToken ct)
    {
        var (host, port) = ParseEndpoint(Config.DeviceId);
        if (host == null)
        {
            SetStatus("Podaj adres w formacie host:port", false);
            ct.WaitHandle.WaitOne(Timeout.Infinite);
            return;
        }

        SetStatus($"Łączenie z {host}:{port}…", false);
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        _socket = socket;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            socket.ConnectAsync(host, port, timeout.Token).AsTask().GetAwaiter().GetResult();
        }
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        SetStatus($"Połączono z {host}:{port}", true);

        var parser = new LineCardParser();
        var buffer = new byte[512];
        while (!ct.IsCancellationRequested)
        {
            if (!socket.Poll(200_000, SelectMode.SelectRead))
            {
                if (parser.FlushIfIdle(DateTime.Now) is { } idleUid) RaiseCard(idleUid);
                continue;
            }
            int n = socket.Receive(buffer);
            if (n == 0)
            {
                SetStatus("Czytnik zamknął połączenie", false);
                break;
            }
            foreach (var ch in Encoding.ASCII.GetString(buffer, 0, n))
                if (parser.Feed(ch, DateTime.Now) is { } uid) RaiseCard(uid);
        }
        _socket = null;
    }

    protected override void OnStopping() => _socket?.Close();

    public static (string? Host, int Port) ParseEndpoint(string value)
    {
        var idx = value.LastIndexOf(':');
        if (idx <= 0 || !int.TryParse(value[(idx + 1)..], out var port) || port is <= 0 or > 65535) return (null, 0);
        return (value[..idx].Trim(), port);
    }
}
