namespace ParallelSeat.Core;

public sealed class StrictSafetyPolicy : ISafetyPolicy
{
    private static readonly HashSet<ProviderKind> BannedProviders =
    [
        ProviderKind.FocusSteal
    ];

    public SafetyMode Mode => SafetyMode.Strict;

    public bool AllowsProvider(ProviderKind provider) =>
        provider is not ProviderKind.FocusSteal and not ProviderKind.Unsupported &&
        !BannedProviders.Contains(provider);

    public bool AllowsCapability(ActionCapability capability) =>
        !capability.HasFlag(ActionCapability.RequiresForeground) &&
        !capability.HasFlag(ActionCapability.RequiresElevation);

    public ActionResult Reject(string actionId, string reason, CapabilitySet? decision = null) =>
        new(
            actionId,
            ActionStatus.RejectedBySafety,
            decision?.Provider ?? ProviderKind.Unsupported,
            reason,
            TimeSpan.Zero,
            null,
            null,
            null,
            null,
            false,
            decision);
}

public sealed class InMemoryAuditLog : IAuditLog
{
    private readonly object _gate = new();
    private readonly List<AuditEntry> _entries = [];

    public void Record(AuditEntry entry)
    {
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }

    public IReadOnlyList<AuditEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToList();
        }
    }
}
