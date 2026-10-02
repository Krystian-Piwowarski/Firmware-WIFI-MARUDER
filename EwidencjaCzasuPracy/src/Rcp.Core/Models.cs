namespace Rcp.Core;

/// <summary>Kierunek zdarzenia: wejście lub wyjście.</summary>
public enum Direction { In = 1, Out = 2 }

/// <summary>Rodzaj fizycznego podłączenia czytnika kart Mifare.</summary>
public enum ReaderKind
{
    /// <summary>Czytnik USB emulujący klawiaturę (HID). Rozróżniany po ścieżce urządzenia (Raw Input).</summary>
    Keyboard = 1,
    /// <summary>Czytnik na porcie COM (RS232 / RS485 / USB-CDC).</summary>
    Serial = 2,
    /// <summary>Czytnik sieciowy TCP/IP wysyłający numer karty jako tekst.</summary>
    Tcp = 3,
    /// <summary>Czytnik PC/SC (np. ACR122U, Omnikey) – UID odczytywany komendą FF CA 00 00 00.</summary>
    PcSc = 4,
}

/// <summary>Jak czytnik interpretuje odbicie karty.</summary>
public enum ReaderMode
{
    In = 1,
    Out = 2,
    /// <summary>Jeden czytnik na wejście i wyjście – kierunek naprzemiennie wg ostatniego zdarzenia.</summary>
    Toggle = 3,
}

public enum EventSource { Reader = 1, Manual = 2 }

public enum AbsenceType
{
    Urlop = 1,
    UrlopNaZadanie = 2,
    L4 = 3,
    Opieka = 4,
    Delegacja = 5,
    UrlopBezplatny = 6,
    UrlopOkolicznosciowy = 7,
    Inne = 9,
}

public static class AbsenceTypes
{
    public static string Code(AbsenceType t) => t switch
    {
        AbsenceType.Urlop => "UW",
        AbsenceType.UrlopNaZadanie => "UŻ",
        AbsenceType.L4 => "L4",
        AbsenceType.Opieka => "OP",
        AbsenceType.Delegacja => "DL",
        AbsenceType.UrlopBezplatny => "UB",
        AbsenceType.UrlopOkolicznosciowy => "UO",
        _ => "IN",
    };

    public static string Name(AbsenceType t) => t switch
    {
        AbsenceType.Urlop => "Urlop wypoczynkowy",
        AbsenceType.UrlopNaZadanie => "Urlop na żądanie",
        AbsenceType.L4 => "Zwolnienie lekarskie (L4)",
        AbsenceType.Opieka => "Opieka",
        AbsenceType.Delegacja => "Delegacja",
        AbsenceType.UrlopBezplatny => "Urlop bezpłatny",
        AbsenceType.UrlopOkolicznosciowy => "Urlop okolicznościowy",
        _ => "Inna nieobecność",
    };

    public static readonly AbsenceType[] All = Enum.GetValues<AbsenceType>();
}

public sealed class Department
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public override string ToString() => Name;
}

public sealed class Employee
{
    public long Id { get; set; }
    public string EmployeeNo { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public long? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public string? Position { get; set; }
    public string? CardUid { get; set; }
    public bool Active { get; set; } = true;
    /// <summary>Dobowa norma czasu pracy w minutach (domyślnie 8 h).</summary>
    public int DailyNormMinutes { get; set; } = 480;
    public string? Notes { get; set; }

    public string FullName => $"{LastName} {FirstName}".Trim();
    public override string ToString() => $"{FullName} ({EmployeeNo})";
}

public sealed class ReaderConfig
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public ReaderKind Kind { get; set; } = ReaderKind.Keyboard;
    public ReaderMode Mode { get; set; } = ReaderMode.In;
    /// <summary>Keyboard: ścieżka urządzenia HID; Serial: np. COM3; Tcp: host:port; PcSc: nazwa czytnika (puste = pierwszy).</summary>
    public string DeviceId { get; set; } = "";
    public int BaudRate { get; set; } = 9600;
    /// <summary>Czytnik podaje numer dziesiętny – zamień na HEX, aby zgadzał się z UID z innych czytników.</summary>
    public bool DecimalToHex { get; set; }
    /// <summary>Odwróć kolejność bajtów UID (niektóre czytniki podają UID od końca).</summary>
    public bool ReverseBytes { get; set; }
    public bool Enabled { get; set; } = true;

    public override string ToString() => Name;
}

public sealed class AttendanceEvent
{
    public long Id { get; set; }
    public long? EmployeeId { get; set; }
    public string CardUid { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public Direction Direction { get; set; }
    public long? ReaderId { get; set; }
    public EventSource Source { get; set; } = EventSource.Reader;
    public string? Note { get; set; }

    // pola dołączane z innych tabel (tylko do odczytu)
    public string? EmployeeName { get; set; }
    public string? EmployeeNo { get; set; }
    public string? ReaderName { get; set; }
    public string? DepartmentName { get; set; }
}

public sealed class Absence
{
    public long Id { get; set; }
    public long EmployeeId { get; set; }
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public AbsenceType Type { get; set; }
    public string? Note { get; set; }

    public bool Covers(DateOnly d) => d >= DateFrom && d <= DateTo;
}

/// <summary>Dodatkowy dzień wolny w firmie (np. za święto w sobotę) albo dzień pracujący.</summary>
public sealed class CompanyDayOff
{
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
}

public sealed class AppSettings
{
    /// <summary>Ignoruj ponowne odbicie tej samej karty przez tyle sekund.</summary>
    public int DebounceSeconds { get; set; } = 30;
    /// <summary>Maksymalna długość zmiany – wyjście później niż po tylu godzinach nie jest parowane z wejściem.</summary>
    public int MaxShiftHours { get; set; } = 16;
    public string CompanyName { get; set; } = "";
}
