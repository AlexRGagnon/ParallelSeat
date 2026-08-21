using ParallelSeat.Core;
using Xunit;

namespace ParallelSeat.UnitTests;

public class SeatStateMachineTests
{
    [Fact]
    public void Create_Attach_Activate_Pause_Resume_Destroy()
    {
        var seat = new Seat("s1");
        Assert.Equal(SeatState.Created, seat.State);

        seat.BeginAttach();
        Assert.Equal(SeatState.Attaching, seat.State);

        var instance = new ApplicationInstanceId(1, DateTime.UtcNow, "test.exe", [1]);
        seat.Activate(instance, [1], [new IntPtr(42)]);
        Assert.Equal(SeatState.Active, seat.State);

        seat.Pause(ConflictReason.HumanCursorInTarget);
        Assert.Equal(SeatState.Paused, seat.State);
        Assert.Equal(ConflictReason.HumanCursorInTarget, seat.ConflictState);

        seat.Resume();
        Assert.Equal(SeatState.Active, seat.State);

        seat.BeginStop();
        seat.Destroy();
        Assert.Equal(SeatState.Destroyed, seat.State);
    }

    [Fact]
    public void InvalidTransition_Throws()
    {
        var seat = new Seat("s2");
        Assert.Throws<InvalidOperationException>(() => seat.Resume());
    }

    [Fact]
    public void Destroy_IsIdempotent_ViaManager()
    {
        var manager = new SeatManager();
        manager.Create("s3");
        manager.Destroy("s3");
        manager.Destroy("s3");
        Assert.Empty(manager.List());
    }

    [Fact]
    public void ApplicationInstanceId_DistinguishesRestarts()
    {
        var a = new ApplicationInstanceId(10, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), "app.exe", [10]);
        var b = new ApplicationInstanceId(10, new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc), "app.exe", [10]);
        Assert.False(a.Matches(b));
    }
}

public class StrictSafetyPolicyTests
{
    [Fact]
    public void Rejects_FocusSteal_And_RequiresForeground()
    {
        var policy = new StrictSafetyPolicy();
        Assert.False(policy.AllowsProvider(ProviderKind.FocusSteal));
        Assert.False(policy.AllowsCapability(ActionCapability.RequiresForeground));
        Assert.True(policy.AllowsProvider(ProviderKind.UiAutomation));
    }
}

public class ActionRouterTests
{
    [Fact]
    public async Task StrictMode_Rejects_When_Foreground_Changes()
    {
        var fg = new MutableForeground(new IntPtr(1));
        var cursor = new FixedCursor(new ScreenPoint(5, 5));
        var provider = new FakeProvider(ProviderKind.UiAutomation, 10, onExecute: () => fg.Value = new IntPtr(2));
        var audit = new InMemoryAuditLog();
        var router = new ActionRouter([provider], new StrictSafetyPolicy(), fg, cursor, audit);
        var seat = new Seat("r1");
        seat.BeginAttach();
        seat.Activate(new ApplicationInstanceId(1, DateTime.UtcNow, "x", [1]), [1], [new IntPtr(1)]);

        var element = new ElementRef("e1", seat.AttachedInstance!, new ElementSelector(AutomationId: "x"), null, null);
        var result = await router.ExecuteAsync(seat, new InvokeAction("a1", element), CancellationToken.None);

        Assert.Equal(ActionStatus.RejectedBySafety, result.Status);
        Assert.True(result.ForegroundChanged);
        Assert.Equal(SeatState.Paused, seat.State);
        Assert.NotEmpty(audit.Snapshot());
    }

    [Fact]
    public async Task NoSilentFallback_When_NoProvider()
    {
        var router = new ActionRouter(
            Array.Empty<IActionProvider>(),
            new StrictSafetyPolicy(),
            new MutableForeground(new IntPtr(1)),
            new FixedCursor(new ScreenPoint(0, 0)),
            new InMemoryAuditLog());
        var seat = new Seat("r2");
        seat.BeginAttach();
        seat.Activate(new ApplicationInstanceId(1, DateTime.UtcNow, "x", [1]), [1], [new IntPtr(1)]);
        var element = new ElementRef("e1", seat.AttachedInstance!, new ElementSelector(AutomationId: "x"), null, null);
        var result = await router.ExecuteAsync(seat, new InvokeAction("a2", element), CancellationToken.None);
        Assert.Equal(ActionStatus.Unsupported, result.Status);
        Assert.Equal(ProviderKind.Unsupported, result.Provider);
    }

    private sealed class MutableForeground(IntPtr value) : IForegroundProbe
    {
        public IntPtr Value = value;
        public IntPtr GetForegroundWindow() => Value;
    }

    private sealed class FixedCursor(ScreenPoint point) : ICursorProbe
    {
        public ScreenPoint GetCursorPosition() => point;
    }

    private sealed class FakeProvider(ProviderKind kind, int priority, Action? onExecute = null) : IActionProvider
    {
        public ProviderKind Kind { get; } = kind;
        public int Priority { get; } = priority;

        public Task<CapabilitySet> ProbeCapabilitiesAsync(ElementRef element, CancellationToken cancellationToken) =>
            Task.FromResult(new CapabilitySet(ActionCapability.CanInvoke, Kind));

        public Task<bool> CanExecuteAsync(SeatAction action, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task ExecuteAsync(SeatAction action, CancellationToken cancellationToken)
        {
            onExecute?.Invoke();
            return Task.CompletedTask;
        }
    }
}

public class ProtocolSerializationTests
{
    [Fact]
    public void RoundTrips_JsonRpcRequest()
    {
        var bytes = ParallelSeat.Protocol.ProtocolSerializer.Serialize(new ParallelSeat.Protocol.JsonRpcRequest
        {
            Id = "1",
            Method = "seat.create",
            CorrelationId = "c"
        });
        var restored = ParallelSeat.Protocol.ProtocolSerializer.Deserialize<ParallelSeat.Protocol.JsonRpcRequest>(bytes);
        Assert.Equal("seat.create", restored.Method);
        Assert.Equal("1", restored.Id);
    }
}
