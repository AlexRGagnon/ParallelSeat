using ParallelSeat.Adapters.Abstractions;
using ParallelSeat.CompatibilityProbe;
using Xunit;

namespace ParallelSeat.CompatibilityTests;

public class CompatibilityInspectorTests
{
    [Fact]
    public void Inspect_CurrentProcess_ReturnsReport()
    {
        var report = CompatibilityInspector.Inspect(Environment.ProcessId);
        Assert.Equal(Environment.ProcessId, report.ProcessId);
        Assert.False(string.IsNullOrWhiteSpace(report.ExecutablePath));
        Assert.True(Enum.IsDefined(typeof(CompatibilityTier), report.Tier));
    }
}
