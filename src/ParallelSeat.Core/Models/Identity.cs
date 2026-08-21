namespace ParallelSeat.Core;

public sealed record ApplicationInstanceId(
    int RootProcessId,
    DateTime RootProcessStartTimeUtc,
    string ExecutablePath,
    IReadOnlyList<int> ProcessTreeIds)
{
    public string Value { get; } =
        $"{RootProcessId}:{RootProcessStartTimeUtc.Ticks}:{ExecutablePath.ToLowerInvariant()}";

    public bool Matches(ApplicationInstanceId other) =>
        string.Equals(Value, other.Value, StringComparison.Ordinal);
}

public sealed record ElementSelector(
    string? AutomationId = null,
    string? Name = null,
    string? ClassName = null,
    string? ControlType = null,
    string? FrameworkId = null,
    IntPtr? TopLevelHwnd = null,
    IntPtr? ChildHwnd = null,
    string? AncestorPath = null,
    string? AdapterId = null);

public sealed record ElementRef(
    string ElementId,
    ApplicationInstanceId InstanceId,
    ElementSelector Selector,
    IntPtr? NativeHwnd,
    string? RuntimeId);

public sealed record TargetLease(
    string SeatId,
    ApplicationInstanceId InstanceId,
    IntPtr? ClaimedWindowHwnd,
    ElementRef? ClaimedElement,
    DateTimeOffset ClaimedAtUtc);

public sealed record CapabilitySet(
    ActionCapability Flags,
    ProviderKind Provider,
    string Notes = "")
{
    public bool Has(ActionCapability capability) => Flags.HasFlag(capability);

    public static CapabilitySet Unsupported(string notes) =>
        new(ActionCapability.None, ProviderKind.Unsupported, notes);
}

public sealed record ScreenPoint(int X, int Y);

public sealed record BoundingRect(int Left, int Top, int Right, int Bottom)
{
    public bool Contains(ScreenPoint point) =>
        point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;
}
