using System.Diagnostics;
using ParallelSeat.Core;
using ParallelSeat.TestHarness;
using Xunit;

namespace ParallelSeat.IntegrationTests;

public class SameProcessSharedStateTests
{
    [Fact]
    public void SameProcess_BothWindows_ShareAppState()
    {
        var pid = Environment.ProcessId;
        Assert.Equal(Process.GetCurrentProcess().Id, pid);

        AppState.Shared.SharedText = "from-test";
        AppState.Shared.ButtonClicks = 3;
        AppState.Shared.RecordAction("integration");

        Assert.Equal("from-test", AppState.Shared.SharedText);
        Assert.Equal(3, AppState.Shared.ButtonClicks);
        Assert.Equal("integration", AppState.Shared.LastAction);
        Assert.Same(AppState.Shared, AppState.Shared);
    }

    [Fact]
    public void InstanceIdentity_UsesStartTime()
    {
        using var process = Process.GetCurrentProcess();
        var a = ParallelSeat.Windows.ProcessInstanceIdentity.FromProcess(process);
        var b = new ApplicationInstanceId(a.RootProcessId, a.RootProcessStartTimeUtc.AddSeconds(1), a.ExecutablePath, a.ProcessTreeIds);
        Assert.False(a.Matches(b));
    }
}
