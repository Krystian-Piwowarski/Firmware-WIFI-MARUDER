using System.Runtime.InteropServices;
using Rcp.Core;

namespace Rcp.App.Readers;

/// <summary>
/// Czytnik PC/SC (ACR122U, ACR1252, Omnikey 5x21 itp.) – obsługa przez systemową bibliotekę winscard.dll.
/// UID karty Mifare odczytywany standardową komendą GET DATA (FF CA 00 00 00).
/// </summary>
public sealed class PcscReaderDriver(ReaderConfig config) : ThreadedReaderDriver(config)
{
    private IntPtr _context;

    protected override void Run(CancellationToken ct)
    {
        Check(SCardEstablishContext(SCARD_SCOPE_USER, IntPtr.Zero, IntPtr.Zero, out _context), "SCardEstablishContext");
        try
        {
            var readerName = Config.DeviceId;
            if (string.IsNullOrWhiteSpace(readerName))
            {
                readerName = ListReaders(_context).FirstOrDefault() ?? "";
                if (readerName.Length == 0)
                {
                    SetStatus("Nie znaleziono czytnika PC/SC", false);
                    return;
                }
            }

            var states = new[] { new SCARD_READERSTATE { szReader = readerName, dwCurrentState = SCARD_STATE_UNAWARE, rgbAtr = new byte[36] } };
            bool cardPresent = false;
            SetStatus($"Gotowy: {readerName}", true);

            while (!ct.IsCancellationRequested)
            {
                int rc = SCardGetStatusChange(_context, 1000, states, 1);
                if (rc == SCARD_E_TIMEOUT) continue;
                if (rc == SCARD_E_CANCELLED) return;
                if (rc != 0)
                {
                    SetStatus($"Czytnik niedostępny ({readerName}): 0x{rc:X8}", false);
                    return; // wątek bazowy odczeka i nawiąże kontekst od nowa
                }

                var evt = states[0].dwEventState;
                states[0].dwCurrentState = evt & ~SCARD_STATE_CHANGED;

                if ((evt & (SCARD_STATE_UNAVAILABLE | SCARD_STATE_UNKNOWN)) != 0)
                {
                    SetStatus($"Czytnik odłączony ({readerName})", false);
                    return;
                }
                SetStatus($"Gotowy: {readerName}", true);

                bool present = (evt & SCARD_STATE_PRESENT) != 0;
                if (present && !cardPresent)
                {
                    var uid = ReadUid(readerName);
                    if (uid != null) RaiseCard(uid);
                }
                cardPresent = present;
            }
        }
        finally
        {
            SCardReleaseContext(_context);
            _context = IntPtr.Zero;
        }
    }

    private string? ReadUid(string readerName)
    {
        int rc = SCardConnect(_context, readerName, SCARD_SHARE_SHARED, SCARD_PROTOCOL_T0 | SCARD_PROTOCOL_T1, out var card, out var protocol);
        if (rc != 0) return null;
        try
        {
            var request = new SCARD_IO_REQUEST { dwProtocol = protocol, cbPciLength = (uint)Marshal.SizeOf<SCARD_IO_REQUEST>() };
            byte[] apdu = { 0xFF, 0xCA, 0x00, 0x00, 0x00 };
            var response = new byte[64];
            int len = response.Length;
            rc = SCardTransmit(card, ref request, apdu, apdu.Length, IntPtr.Zero, response, ref len);
            if (rc != 0 || len < 2 || response[len - 2] != 0x90 || response[len - 1] != 0x00) return null;
            return CardUid.FromBytes(response.AsSpan(0, len - 2));
        }
        finally
        {
            SCardDisconnect(card, SCARD_LEAVE_CARD);
        }
    }

    protected override void OnStopping()
    {
        if (_context != IntPtr.Zero) SCardCancel(_context);
    }

    /// <summary>Lista czytników PC/SC zainstalowanych w systemie (do okna konfiguracji).</summary>
    public static List<string> ListReaders()
    {
        if (SCardEstablishContext(SCARD_SCOPE_USER, IntPtr.Zero, IntPtr.Zero, out var ctx) != 0) return new();
        try { return ListReaders(ctx); }
        finally { SCardReleaseContext(ctx); }
    }

    private static List<string> ListReaders(IntPtr ctx)
    {
        int size = 0;
        if (SCardListReaders(ctx, null, null, ref size) != 0 || size == 0) return new();
        var buffer = new char[size];
        if (SCardListReaders(ctx, null, buffer, ref size) != 0) return new();
        return new string(buffer, 0, size).Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static void Check(int rc, string fn)
    {
        if (rc == SCARD_E_NO_SERVICE) throw new InvalidOperationException("Usługa Karta inteligentna (SCardSvr) nie działa");
        if (rc != 0) throw new InvalidOperationException($"{fn}: 0x{rc:X8}");
    }

    // ---- winscard.dll ----
    private const uint SCARD_SCOPE_USER = 0;
    private const uint SCARD_SHARE_SHARED = 2;
    private const uint SCARD_PROTOCOL_T0 = 1, SCARD_PROTOCOL_T1 = 2;
    private const uint SCARD_LEAVE_CARD = 0;
    private const uint SCARD_STATE_UNAWARE = 0, SCARD_STATE_CHANGED = 0x2, SCARD_STATE_UNKNOWN = 0x4,
        SCARD_STATE_UNAVAILABLE = 0x8, SCARD_STATE_PRESENT = 0x20;
    private const int SCARD_E_TIMEOUT = unchecked((int)0x8010000A);
    private const int SCARD_E_CANCELLED = unchecked((int)0x80100002);
    private const int SCARD_E_NO_SERVICE = unchecked((int)0x8010001D);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SCARD_READERSTATE
    {
        public string szReader;
        public IntPtr pvUserData;
        public uint dwCurrentState;
        public uint dwEventState;
        public uint cbAtr;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)] public byte[] rgbAtr;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SCARD_IO_REQUEST
    {
        public uint dwProtocol;
        public uint cbPciLength;
    }

    [DllImport("winscard.dll")]
    private static extern int SCardEstablishContext(uint dwScope, IntPtr r1, IntPtr r2, out IntPtr phContext);

    [DllImport("winscard.dll")]
    private static extern int SCardReleaseContext(IntPtr hContext);

    [DllImport("winscard.dll")]
    private static extern int SCardCancel(IntPtr hContext);

    [DllImport("winscard.dll", EntryPoint = "SCardListReadersW", CharSet = CharSet.Unicode)]
    private static extern int SCardListReaders(IntPtr hContext, string? groups, char[]? readers, ref int pcchReaders);

    [DllImport("winscard.dll", EntryPoint = "SCardGetStatusChangeW", CharSet = CharSet.Unicode)]
    private static extern int SCardGetStatusChange(IntPtr hContext, uint dwTimeout, [In, Out] SCARD_READERSTATE[] states, int cReaders);

    [DllImport("winscard.dll", EntryPoint = "SCardConnectW", CharSet = CharSet.Unicode)]
    private static extern int SCardConnect(IntPtr hContext, string szReader, uint dwShareMode, uint dwPreferredProtocols,
        out IntPtr phCard, out uint pdwActiveProtocol);

    [DllImport("winscard.dll")]
    private static extern int SCardDisconnect(IntPtr hCard, uint dwDisposition);

    [DllImport("winscard.dll")]
    private static extern int SCardTransmit(IntPtr hCard, ref SCARD_IO_REQUEST pioSendPci, byte[] pbSendBuffer, int cbSendLength,
        IntPtr pioRecvPci, byte[] pbRecvBuffer, ref int pcbRecvLength);
}
