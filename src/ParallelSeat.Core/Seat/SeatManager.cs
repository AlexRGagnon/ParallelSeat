using System.Collections.Concurrent;

namespace ParallelSeat.Core;

public sealed class SeatManager
{
    private readonly ConcurrentDictionary<string, Seat> _seats = new(StringComparer.Ordinal);
    private readonly IOverlayController? _overlay;
    private readonly TimeSpan _heartbeatTimeout;

    public SeatManager(IOverlayController? overlay = null, TimeSpan? heartbeatTimeout = null)
    {
        _overlay = overlay;
        _heartbeatTimeout = heartbeatTimeout ?? TimeSpan.FromSeconds(15);
    }

    public Seat Create(string seatId, SafetyMode mode = SafetyMode.Strict)
    {
        var seat = new Seat(seatId, mode);
        if (!_seats.TryAdd(seatId, seat))
        {
            throw new InvalidOperationException($"Seat '{seatId}' already exists.");
        }

        return seat;
    }

    public Seat Get(string seatId) =>
        _seats.TryGetValue(seatId, out var seat)
            ? seat
            : throw new KeyNotFoundException($"Seat '{seatId}' not found.");

    public bool TryGet(string seatId, out Seat? seat) => _seats.TryGetValue(seatId, out seat);

    public IReadOnlyCollection<Seat> List() => _seats.Values.ToList();

    public void Heartbeat(string seatId) => Get(seatId).Heartbeat();

    public void EmergencyStop()
    {
        foreach (var seat in _seats.Values.ToList())
        {
            seat.Pause(ConflictReason.EmergencyStop);
            seat.BeginStop();
            seat.Destroy();
            _seats.TryRemove(seat.SeatId, out _);
        }

        _overlay?.HideAll();
    }

    public void Destroy(string seatId)
    {
        if (!_seats.TryGetValue(seatId, out var seat))
        {
            return;
        }

        seat.BeginStop();
        seat.Destroy();
        _overlay?.Hide(seatId);
        _seats.TryRemove(seatId, out _);
    }

    public IReadOnlyList<string> SweepTimedOutSeats(DateTimeOffset utcNow)
    {
        var removed = new List<string>();
        foreach (var seat in _seats.Values)
        {
            if (utcNow - seat.LastHeartbeatUtc > _heartbeatTimeout &&
                seat.State is not (SeatState.Destroyed or SeatState.Stopping))
            {
                seat.Pause(ConflictReason.HeartbeatTimeout);
                Destroy(seat.SeatId);
                removed.Add(seat.SeatId);
            }
        }

        return removed;
    }
}
