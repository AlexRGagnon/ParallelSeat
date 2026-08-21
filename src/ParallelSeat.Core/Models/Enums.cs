namespace ParallelSeat.Core;

public enum SeatState
{
    Created,
    Attaching,
    Active,
    Paused,
    Stopping,
    Destroyed,
    Faulted
}

public enum SafetyMode
{
    Strict,
    CompatibilityFocusStealOptIn
}

public enum ConflictReason
{
    None,
    HumanCursorInTarget,
    HumanClickedClaimedWindow,
    HumanFocusInClaimedControl,
    HumanTypingInClaimedControl,
    ModalDialogOpen,
    TargetClosed,
    ApplicationStateInvalidated,
    EmergencyStop,
    HeartbeatTimeout
}

public enum ProviderKind
{
    ApplicationAdapter,
    UiAutomation,
    Win32,
    ProcessBridge,
    FocusSteal,
    Unsupported
}

[Flags]
public enum ActionCapability
{
    None = 0,
    CanInvoke = 1 << 0,
    CanSetValue = 1 << 1,
    CanSelect = 1 << 2,
    CanToggle = 1 << 3,
    CanExpandCollapse = 1 << 4,
    CanScroll = 1 << 5,
    CanPointerMove = 1 << 6,
    CanPointerClick = 1 << 7,
    CanDoubleClick = 1 << 8,
    CanDrag = 1 << 9,
    CanSendText = 1 << 10,
    CanSendKeySequence = 1 << 11,
    RequiresForeground = 1 << 12,
    RequiresProcessBridge = 1 << 13,
    RequiresElevation = 1 << 14
}
