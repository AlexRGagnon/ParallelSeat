namespace ParallelSeat.Core;

public interface IActionProvider
{
    ProviderKind Kind { get; }
    int Priority { get; }
    Task<CapabilitySet> ProbeCapabilitiesAsync(ElementRef element, CancellationToken cancellationToken);
    Task<bool> CanExecuteAsync(SeatAction action, CancellationToken cancellationToken);
    Task ExecuteAsync(SeatAction action, CancellationToken cancellationToken);
}

public interface IForegroundProbe
{
    IntPtr GetForegroundWindow();
}

public interface ICursorProbe
{
    ScreenPoint GetCursorPosition();
}

public interface IOverlayController
{
    void Show(string seatId, ScreenPoint logicalCursor, BoundingRect? targetOutline, string label = "AI");
    void Update(string seatId, ScreenPoint logicalCursor, BoundingRect? targetOutline, string feedback = "");
    void Hide(string seatId);
    void HideAll();
}

public interface IAuditLog
{
    void Record(AuditEntry entry);
    IReadOnlyList<AuditEntry> Snapshot();
}

public sealed record AuditEntry(
    DateTimeOffset Timestamp,
    string ActionId,
    string SeatId,
    string ApplicationInstanceId,
    string? TargetWindow,
    string? TargetElement,
    string RequestedAction,
    ProviderKind ResolvedProvider,
    string CapabilityDecision,
    string Preconditions,
    ActionStatus Result,
    string? Error,
    TimeSpan Duration,
    bool NativeForegroundChanged,
    bool NativeCursorChanged,
    bool HumanConflictDetected);

public interface IConflictMonitor
{
    event EventHandler<ConflictDetectedEventArgs>? ConflictDetected;
    void Start(string seatId, TargetLease lease);
    void Stop(string seatId);
}

public sealed class ConflictDetectedEventArgs : EventArgs
{
    public required string SeatId { get; init; }
    public required ConflictReason Reason { get; init; }
}

public interface ISafetyPolicy
{
    SafetyMode Mode { get; }
    bool AllowsProvider(ProviderKind provider);
    bool AllowsCapability(ActionCapability capability);
    ActionResult Reject(string actionId, string reason, CapabilitySet? decision = null);
}
