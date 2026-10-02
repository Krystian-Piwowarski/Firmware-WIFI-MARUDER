namespace Rcp.Core.Services;

public enum ScanStatus { Registered, UnknownCard, InactiveEmployee, Ignored }

public sealed record ScanResult(
    ScanStatus Status,
    string CardUid,
    Direction? Direction,
    Employee? Employee,
    ReaderConfig Reader,
    DateTime Time,
    AttendanceEvent? Event)
{
    public string Message => Status switch
    {
        ScanStatus.Registered => $"{(Direction == Core.Direction.In ? "WEJŚCIE" : "WYJŚCIE")}: {Employee!.FullName}",
        ScanStatus.UnknownCard => $"Nieznana karta {CardUid}",
        ScanStatus.InactiveEmployee => $"Karta nieaktywnego pracownika: {Employee!.FullName}",
        _ => $"Powtórne odbicie zignorowane: {Employee?.FullName ?? CardUid}",
    };
}

/// <summary>Zamienia odczyt karty z czytnika na zdarzenie wejścia/wyjścia w bazie.</summary>
public sealed class ScanProcessor(RcpContext ctx)
{
    private readonly Dictionary<string, DateTime> _lastScan = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public ScanResult Process(ReaderConfig reader, string rawUid, DateTime now)
    {
        var uid = CardUid.Normalize(rawUid, reader.DecimalToHex, reader.ReverseBytes);
        var employee = ctx.Employees.GetByCard(uid);

        lock (_lock)
        {
            var debounce = TimeSpan.FromSeconds(Math.Max(0, ctx.Settings.DebounceSeconds));
            if (_lastScan.TryGetValue(uid, out var last) && now - last < debounce && now >= last)
                return new ScanResult(ScanStatus.Ignored, uid, null, employee, reader, now, null);
            _lastScan[uid] = now;
            if (_lastScan.Count > 10_000) Prune(now, debounce);
        }

        var direction = reader.Mode switch
        {
            ReaderMode.In => Direction.In,
            ReaderMode.Out => Direction.Out,
            _ => ToggleDirection(employee, now),
        };

        var evt = new AttendanceEvent
        {
            EmployeeId = employee?.Id,
            CardUid = uid,
            Timestamp = now,
            Direction = direction,
            ReaderId = reader.Id == 0 ? null : reader.Id,
            Source = EventSource.Reader,
            Note = employee is { Active: false } ? "Pracownik nieaktywny" : null,
        };
        ctx.Events.Insert(evt);

        var status = employee == null ? ScanStatus.UnknownCard
            : !employee.Active ? ScanStatus.InactiveEmployee
            : ScanStatus.Registered;
        return new ScanResult(status, uid, direction, employee, reader, now, evt);
    }

    private Direction ToggleDirection(Employee? employee, DateTime now)
    {
        if (employee == null) return Direction.In;
        var last = ctx.Events.LastForEmployee(employee.Id, now - ctx.MaxShift);
        return last?.Direction == Direction.In ? Direction.Out : Direction.In;
    }

    private void Prune(DateTime now, TimeSpan debounce)
    {
        foreach (var key in _lastScan.Where(kv => now - kv.Value > debounce).Select(kv => kv.Key).ToList())
            _lastScan.Remove(key);
    }
}
