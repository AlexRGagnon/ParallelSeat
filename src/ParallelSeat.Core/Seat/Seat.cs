using System.Collections.Concurrent;

namespace ParallelSeat.Core;

public sealed class Seat
{
    private readonly object _gate = new();
    private readonly ConcurrentQueue<SeatAction> _pending = new();

    public Seat(string seatId, SafetyMode safetyMode = SafetyMode.Strict)
    {
        SeatId = seatId;
        SafetyMode = safetyMode;
        State = SeatState.Created;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        LastHeartbeatUtc = CreatedAtUtc;
    }

    public string SeatId { get; }
    public SafetyMode SafetyMode { get; }
    public SeatState State { get; private set; }
    public ApplicationInstanceId? AttachedInstance { get; private set; }
    public IReadOnlyList<int> AllowedProcessIds { get; private set; } = [];
    public IReadOnlyList<IntPtr> AllowedTopLevelHwnds { get; private set; } = [];
    public TargetLease? Lease { get; private set; }
    public ScreenPoint LogicalCursor { get; private set; } = new(0, 0);
    public ProviderKind? CurrentProvider { get; private set; }
    public CapabilitySet? CurrentCapabilities { get; private set; }
    public SeatAction? CurrentAction { get; private set; }
    public ConflictReason ConflictState { get; private set; } = ConflictReason.None;
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset LastHeartbeatUtc { get; private set; }
    public string? FaultReason { get; private set; }

    public IReadOnlyCollection<SeatAction> PendingActions => _pending.ToArray();

    public void Heartbeat() => LastHeartbeatUtc = DateTimeOffset.UtcNow;

    public void BeginAttach() => Transition(SeatState.Created, SeatState.Attaching, alsoFrom: SeatState.Faulted);

    public void Activate(ApplicationInstanceId instance, IReadOnlyList<int> processIds, IReadOnlyList<IntPtr> hwnds)
    {
        lock (_gate)
        {
            EnsureTransition(SeatState.Attaching, SeatState.Active);
            AttachedInstance = instance;
            AllowedProcessIds = processIds.ToList();
            AllowedTopLevelHwnds = hwnds.ToList();
            State = SeatState.Active;
        }
    }

    public void Claim(TargetLease lease)
    {
        lock (_gate)
        {
            EnsureActiveOrPaused();
            Lease = lease;
        }
    }

    public void ReleaseClaim()
    {
        lock (_gate)
        {
            Lease = null;
            CurrentCapabilities = null;
            CurrentProvider = null;
        }
    }

    public void SetLogicalCursor(ScreenPoint point) => LogicalCursor = point;

    public void SetCapabilities(ProviderKind provider, CapabilitySet capabilities)
    {
        CurrentProvider = provider;
        CurrentCapabilities = capabilities;
    }

    public void Enqueue(SeatAction action)
    {
        lock (_gate)
        {
            if (State is not SeatState.Active)
            {
                throw new InvalidOperationException($"Cannot enqueue action while seat is {State}.");
            }

            _pending.Enqueue(action);
        }
    }

    public bool TryDequeue(out SeatAction? action) => _pending.TryDequeue(out action);

    public void BeginAction(SeatAction action) => CurrentAction = action;

    public void CompleteAction() => CurrentAction = null;

    public void Pause(ConflictReason reason)
    {
        lock (_gate)
        {
            if (State is SeatState.Destroyed or SeatState.Stopping)
            {
                return;
            }

            ConflictState = reason;
            State = SeatState.Paused;
            DrainUnsafeQueue();
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            EnsureTransition(SeatState.Paused, SeatState.Active);
            ConflictState = ConflictReason.None;
            State = SeatState.Active;
        }
    }

    public void BeginStop()
    {
        lock (_gate)
        {
            if (State is SeatState.Destroyed or SeatState.Stopping)
            {
                return;
            }

            State = SeatState.Stopping;
            DrainUnsafeQueue();
            CurrentAction = null;
        }
    }

    public void Destroy()
    {
        lock (_gate)
        {
            State = SeatState.Destroyed;
            Lease = null;
            CurrentAction = null;
            CurrentCapabilities = null;
            CurrentProvider = null;
            DrainUnsafeQueue();
        }
    }

    public void Fault(string reason)
    {
        lock (_gate)
        {
            FaultReason = reason;
            State = SeatState.Faulted;
            DrainUnsafeQueue();
            CurrentAction = null;
        }
    }

    private void DrainUnsafeQueue()
    {
        while (_pending.TryDequeue(out _))
        {
        }
    }

    private void EnsureActiveOrPaused()
    {
        if (State is not (SeatState.Active or SeatState.Paused))
        {
            throw new InvalidOperationException($"Seat must be Active or Paused, was {State}.");
        }
    }

    private void Transition(SeatState from, SeatState to, SeatState? alsoFrom = null)
    {
        lock (_gate)
        {
            if (State != from && (alsoFrom is null || State != alsoFrom))
            {
                throw new InvalidOperationException($"Invalid transition from {State} to {to}.");
            }

            State = to;
        }
    }

    private void EnsureTransition(SeatState from, SeatState to)
    {
        if (State != from)
        {
            throw new InvalidOperationException($"Invalid transition from {State} to {to}; expected {from}.");
        }
    }
}
