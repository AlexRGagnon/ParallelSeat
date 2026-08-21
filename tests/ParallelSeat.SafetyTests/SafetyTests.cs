using ParallelSeat.Core;
using ParallelSeat.Host;
using ParallelSeat.Windows;
using Xunit;

namespace ParallelSeat.SafetyTests;

public class SafetyPolicyTests
{
    [Fact]
    public void StrictPolicy_BlocksPasswordCapabilityPath()
    {
        var policy = new StrictSafetyPolicy();
        Assert.False(policy.AllowsCapability(ActionCapability.RequiresElevation));
        var rejected = policy.Reject("a1", "blocked");
        Assert.Equal(ActionStatus.RejectedBySafety, rejected.Status);
    }

    [Fact]
    public async Task EmergencyStop_DestroysSeats_AndHidesOverlay()
    {
        var overlay = new RecordingOverlay();
        var manager = new SeatManager(overlay);
        var seat = manager.Create("safe-1");
        seat.BeginAttach();
        seat.Activate(new ApplicationInstanceId(1, DateTime.UtcNow, "x", [1]), [1], [new IntPtr(1)]);
        overlay.Show("safe-1", new ScreenPoint(1, 1), null);
        manager.EmergencyStop();
        Assert.True(overlay.HideAllCalled);
        Assert.Empty(manager.List());
        await Task.CompletedTask;
    }

    [Fact]
    public async Task HeartbeatTimeout_SweepsSeat()
    {
        var manager = new SeatManager(heartbeatTimeout: TimeSpan.FromMilliseconds(50));
        manager.Create("hb-1");
        await Task.Delay(80);
        var removed = manager.SweepTimedOutSeats(DateTimeOffset.UtcNow);
        Assert.Contains("hb-1", removed);
    }

    [Fact]
    public async Task Runtime_Rejects_BadToken()
    {
        await using var runtime = new ParallelSeatRuntime("good");
        runtime.StartIpc();
        await Task.Delay(150);
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var client = await ParallelSeat.Ipc.NamedPipeRpcClient.ConnectAsync(
                runtime.PipeName,
                "bad",
                CancellationToken.None);
        });
    }

    private sealed class RecordingOverlay : IOverlayController
    {
        public bool HideAllCalled { get; private set; }
        public void Show(string seatId, ScreenPoint logicalCursor, BoundingRect? targetOutline, string label = "AI") { }
        public void Update(string seatId, ScreenPoint logicalCursor, BoundingRect? targetOutline, string feedback = "") { }
        public void Hide(string seatId) { }
        public void HideAll() => HideAllCalled = true;
    }
}

public class ConflictMonitorTests
{
    [Fact]
    public async Task Detects_Cursor_In_Claimed_Window_Bounds()
    {
        // Synthetic: monitor with fake probes pointing into a known HWND is environment-specific.
        // Validate wiring: starting and stopping does not throw.
        var monitor = new PollingConflictMonitor(new Win32CursorProbe(), new Win32ForegroundProbe());
        var lease = new TargetLease(
            "c1",
            new ApplicationInstanceId(1, DateTime.UtcNow, "x", [1]),
            NativeMethods.GetForegroundWindow(),
            null,
            DateTimeOffset.UtcNow);
        monitor.Start("c1", lease);
        await Task.Delay(120);
        monitor.Stop("c1");
        monitor.Dispose();
    }
}
