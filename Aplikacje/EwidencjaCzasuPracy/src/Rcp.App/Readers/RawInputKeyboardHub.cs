using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Rcp.App.Readers;

/// <summary>
/// Nasłuch czytników USB „klawiaturowych” przez Windows Raw Input.
/// Każde urządzenie HID ma własną ścieżkę, więc można odróżnić czytnik WEJŚCIA od czytnika WYJŚCIA
/// (i od zwykłej klawiatury). Działa również, gdy okno programu nie jest aktywne (RIDEV_INPUTSINK).
/// Gdy program jest aktywny, znaki z czytników są usuwane z kolejki, aby nie trafiały do pól tekstowych.
/// </summary>
public sealed class RawInputKeyboardHub : NativeWindow, IMessageFilter, IDisposable
{
    private sealed class DeviceBuffer
    {
        public readonly StringBuilder Text = new();
        public long LastTick;
    }

    private readonly Dictionary<IntPtr, DeviceBuffer> _buffers = new();
    private readonly Dictionary<IntPtr, string> _names = new();
    private readonly Queue<(ushort VKey, long Tick)> _suppress = new();
    private HashSet<string> _readerDevices = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Zakończony odczyt: (ścieżka urządzenia, tekst). Zgłaszane w wątku UI.</summary>
    public event Action<string, string>? Scan;

    public RawInputKeyboardHub()
    {
        CreateHandle(new CreateParams { Caption = "EwidencjaRCP.RawInput" });
        var device = new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x06, dwFlags = RIDEV_INPUTSINK, hwndTarget = Handle };
        if (!RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Nie udało się zarejestrować Raw Input");
        Application.AddMessageFilter(this);
    }

    /// <summary>Tryb wykrywania – klawisze ze wszystkich urządzeń są blokowane (Enter z czytnika nie „kliknie” przycisku).</summary>
    public bool CaptureMode { get; set; }

    /// <summary>Urządzenia skonfigurowane jako czytniki – ich znaki nie trafią do pól tekstowych programu.</summary>
    public void SetReaderDevices(IEnumerable<string> devicePaths)
        => _readerDevices = new HashSet<string>(devicePaths.Where(p => !string.IsNullOrEmpty(p)), StringComparer.OrdinalIgnoreCase);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_INPUT) Process(m.LParam);
        base.WndProc(ref m);
    }

    private void Process(IntPtr hRawInput)
    {
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        uint size = 0;
        GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0) return;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(hRawInput, RID_INPUT, buffer, ref size, headerSize) != size) return;
            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
            if (header.dwType != RIM_TYPEKEYBOARD) return;
            var kb = Marshal.PtrToStructure<RAWKEYBOARD>(buffer + (int)headerSize);
            if ((kb.Flags & RI_KEY_BREAK) != 0) return;
            if (kb.Message is not (WM_KEYDOWN or WM_SYSKEYDOWN)) return;
            HandleKey(header.hDevice, kb.VKey);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void HandleKey(IntPtr device, ushort vkey)
    {
        var name = DeviceName(device);
        long now = Environment.TickCount64;
        if (CaptureMode || _readerDevices.Contains(name)) _suppress.Enqueue((vkey, now));

        if (!_buffers.TryGetValue(device, out var buf)) _buffers[device] = buf = new DeviceBuffer();
        // czytnik „wpisuje” znaki bardzo szybko – dłuższa przerwa oznacza nowy odczyt
        if (now - buf.LastTick > 500) buf.Text.Clear();
        buf.LastTick = now;

        if (vkey is VK_RETURN or VK_TAB)
        {
            var text = buf.Text.ToString();
            buf.Text.Clear();
            if (text.Length >= 4) Scan?.Invoke(name, text);
            return;
        }

        char? ch = vkey switch
        {
            >= 0x30 and <= 0x39 => (char)vkey,               // 0-9
            >= 0x60 and <= 0x69 => (char)('0' + vkey - 0x60), // klawiatura numeryczna
            >= 0x41 and <= 0x5A => (char)vkey,               // A-Z
            _ => null,
        };
        if (ch != null) buf.Text.Append(ch.Value);
        if (buf.Text.Length > 64) buf.Text.Clear();
    }

    /// <summary>Usuwa z kolejki komunikaty klawiatury pochodzące z czytników (gdy program jest aktywny).</summary>
    public bool PreFilterMessage(ref Message m)
    {
        // zwolnienia klawiszy (WM_KEYUP) bez wciśnięcia są nieszkodliwe – usuwamy tylko wciśnięcia
        if (_suppress.Count == 0 || m.Msg is not (WM_KEYDOWN or WM_SYSKEYDOWN)) return false;
        long now = Environment.TickCount64;
        while (_suppress.Count > 0 && now - _suppress.Peek().Tick > 300) _suppress.Dequeue();
        if (_suppress.Count == 0) return false;
        if ((ushort)m.WParam != _suppress.Peek().VKey) return false;
        _suppress.Dequeue();
        return true;
    }

    private string DeviceName(IntPtr device)
    {
        if (_names.TryGetValue(device, out var name)) return name;
        uint size = 0;
        GetRawInputDeviceInfo(device, RIDI_DEVICENAME, IntPtr.Zero, ref size);
        name = device.ToString();
        if (size > 0)
        {
            var p = Marshal.AllocHGlobal((int)size * 2);
            try
            {
                if ((int)GetRawInputDeviceInfo(device, RIDI_DEVICENAME, p, ref size) > 0)
                    name = Marshal.PtrToStringUni(p) ?? name;
            }
            finally { Marshal.FreeHGlobal(p); }
        }
        _names[device] = name;
        return name;
    }

    public void Dispose()
    {
        Application.RemoveMessageFilter(this);
        var device = new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x06, dwFlags = RIDEV_REMOVE, hwndTarget = IntPtr.Zero };
        RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        DestroyHandle();
    }

    // ---- user32.dll ----
    private const int WM_INPUT = 0x00FF, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
    private const uint RID_INPUT = 0x10000003, RIDI_DEVICENAME = 0x20000007, RIM_TYPEKEYBOARD = 1;
    private const uint RIDEV_INPUTSINK = 0x00000100, RIDEV_REMOVE = 0x00000001;
    private const ushort RI_KEY_BREAK = 1, VK_RETURN = 0x0D, VK_TAB = 0x09;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint command, IntPtr data, ref uint size);
}
