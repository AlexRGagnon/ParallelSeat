namespace ParallelSeat.Core;

public abstract record SeatAction(string ActionId)
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
}

public sealed record InvokeAction(string ActionId, ElementRef Target) : SeatAction(ActionId);
public sealed record SetValueAction(string ActionId, ElementRef Target, string Value) : SeatAction(ActionId);
public sealed record ToggleAction(string ActionId, ElementRef Target) : SeatAction(ActionId);
public sealed record SelectAction(string ActionId, ElementRef Target) : SeatAction(ActionId);
public sealed record ExpandCollapseAction(string ActionId, ElementRef Target, bool Expand) : SeatAction(ActionId);
public sealed record ScrollAction(string ActionId, ElementRef Target, double HorizontalPercent, double VerticalPercent) : SeatAction(ActionId);
public sealed record ClickLogicalPointAction(string ActionId, ElementRef? Target, ScreenPoint Point) : SeatAction(ActionId);
public sealed record TypeTextAction(string ActionId, ElementRef Target, string Text) : SeatAction(ActionId);
public sealed record PressKeyAction(string ActionId, ElementRef? Target, string Key) : SeatAction(ActionId);
public sealed record SendChordAction(string ActionId, ElementRef? Target, IReadOnlyList<string> Keys) : SeatAction(ActionId);
public sealed record DragAction(string ActionId, ScreenPoint Start, ScreenPoint End) : SeatAction(ActionId);

public enum ActionStatus
{
    Succeeded,
    Failed,
    Cancelled,
    Unsupported,
    RejectedBySafety,
    StaleTarget,
    TimedOut,
    ConflictPaused
}

public sealed record ActionResult(
    string ActionId,
    ActionStatus Status,
    ProviderKind Provider,
    string? Message,
    TimeSpan Duration,
    IntPtr? ForegroundBefore,
    IntPtr? ForegroundAfter,
    ScreenPoint? CursorBefore,
    ScreenPoint? CursorAfter,
    bool HumanConflictDetected,
    CapabilitySet? CapabilityDecision)
{
    public bool ForegroundChanged =>
        ForegroundBefore.HasValue && ForegroundAfter.HasValue && ForegroundBefore != ForegroundAfter;

    public bool CursorChanged =>
        CursorBefore is { } before && CursorAfter is { } after &&
        (before.X != after.X || before.Y != after.Y);

    public bool ViolatedStrictNoFocusSteal => ForegroundChanged || CursorChanged;
}
