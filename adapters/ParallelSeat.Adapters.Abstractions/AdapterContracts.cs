using ParallelSeat.Core;

namespace ParallelSeat.Adapters.Abstractions;

public interface IApplicationAdapter : IActionProvider
{
    string AdapterId { get; }
    bool CanAttach(ApplicationInstanceId instance);
}

public enum CompatibilityTier
{
    A_OfficialAdapter,
    B_FullUiAutomation,
    C_PartialUiaPlusWin32,
    D_RequiresProcessBridge,
    E_FocusStealOnly,
    F_Unsupported
}

public sealed record CompatibilityReport(
    string ExecutablePath,
    int ProcessId,
    CompatibilityTier Tier,
    string FrameworkSummary,
    IReadOnlyList<string> AvailablePatterns,
    IReadOnlyList<string> Notes,
    bool RequiresForeground,
    bool IsElevated,
    bool IsProtected);
