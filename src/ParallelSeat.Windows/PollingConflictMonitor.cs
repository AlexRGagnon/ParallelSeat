using ParallelSeat.Core;

namespace ParallelSeat.Windows;

/// <summary>
/// Polling-based human conflict monitor (user-mode). Avoids system-wide low-level hooks for MVP.
/// </summary>
public sealed class PollingConflictMonitor : IConflictMonitor, IDisposable
{
    private readonly ICursorProbe _cursor;
    private readonly IForegroundProbe _foreground;
    private readonly Dictionary<string, TargetLease> _leases = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private Timer? _timer;
    private bool _disposed;

    public PollingConflictMonitor(ICursorProbe cursor, IForegroundProbe foreground)
    {
        _cursor = cursor;
        _foreground = foreground;
    }

    public event EventHandler<ConflictDetectedEventArgs>? ConflictDetected;

    public void Start(string seatId, TargetLease lease)
    {
        lock (_gate)
        {
            _leases[seatId] = lease;
            _timer ??= new Timer(OnTick, null, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
        }
    }

    public void Stop(string seatId)
    {
        lock (_gate)
        {
            _leases.Remove(seatId);
            if (_leases.Count == 0)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }
    }

    private void OnTick(object? state)
    {
        List<(string SeatId, TargetLease Lease)> snapshot;
        lock (_gate)
        {
            snapshot = _leases.Select(kv => (kv.Key, kv.Value)).ToList();
        }

        var cursor = _cursor.GetCursorPosition();
        var foreground = _foreground.GetForegroundWindow();

        foreach (var (seatId, lease) in snapshot)
        {
            if (lease.ClaimedWindowHwnd is { } hwnd && ProcessInstanceIdentity.IsAlive(hwnd))
            {
                var bounds = ProcessInstanceIdentity.GetBounds(hwnd);
                if (bounds is not null && bounds.Contains(cursor) && IsPrimaryMouseDown())
                {
                    ConflictDetected?.Invoke(this, new ConflictDetectedEventArgs
                    {
                        SeatId = seatId,
                        Reason = ConflictReason.HumanClickedClaimedWindow
                    });
                    continue;
                }

                if (bounds is not null && bounds.Contains(cursor))
                {
                    ConflictDetected?.Invoke(this, new ConflictDetectedEventArgs
                    {
                        SeatId = seatId,
                        Reason = ConflictReason.HumanCursorInTarget
                    });
                    continue;
                }

                if (foreground == hwnd)
                {
                    ConflictDetected?.Invoke(this, new ConflictDetectedEventArgs
                    {
                        SeatId = seatId,
                        Reason = ConflictReason.HumanFocusInClaimedControl
                    });
                }
            }
            else if (lease.ClaimedWindowHwnd is not null)
            {
                ConflictDetected?.Invoke(this, new ConflictDetectedEventArgs
                {
                    SeatId = seatId,
                    Reason = ConflictReason.TargetClosed
                });
            }
        }
    }

    private static bool IsPrimaryMouseDown() => (NativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer?.Dispose();
    }
}
