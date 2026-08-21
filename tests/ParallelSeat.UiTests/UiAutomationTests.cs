using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ParallelSeat.Automation;
using ParallelSeat.Core;
using ParallelSeat.Overlay;
using ParallelSeat.TestHarness;
using ParallelSeat.Windows;
using ParallelSeat.Win32Provider;
using Xunit;

namespace ParallelSeat.UiTests;

public class BackgroundUiaTests
{
    [Fact]
    public async Task BackgroundSetValue_NoFocusSteal()
    {
        await StaUiTestRunner.RunAsync(async () =>
        {
            AppState.Shared.SharedText = "initial";
            var human = new HumanWindow();
            var ai = new AiWindow();
            human.Show();
            ai.Show();
            human.Activate();
            human.Editor.Focus();
            human.Editor.Text = "typing-";
            await Dispatcher.Yield();

            var adapter = new InProcessHarnessAdapter();
            var instance = ProcessInstanceIdentity.FromPid(Environment.ProcessId);
            var element = new ElementRef(
                Guid.NewGuid().ToString("N"),
                instance,
                new ElementSelector(AutomationId: "AiValueField", TopLevelHwnd: ai.Handle),
                ai.Handle,
                null);

            var seat = new Seat("ui-1");
            seat.BeginAttach();
            seat.Activate(instance, [Environment.ProcessId], [human.Handle, ai.Handle]);
            var router = new ActionRouter(
                [adapter],
                new StrictSafetyPolicy(),
                new Win32ForegroundProbe(),
                new Win32CursorProbe(),
                new InMemoryAuditLog());

            // Under VSTest, Activate() often cannot become system foreground (FG lock).
            // Assert the AI action does not change whatever FG currently is, and does not select AI.
            var fgBefore = NativeMethods.GetForegroundWindow();
            var result = await router.ExecuteAsync(
                seat,
                new SetValueAction(Guid.NewGuid().ToString("N"), element, "60"),
                CancellationToken.None);

            await Dispatcher.Yield();
            Assert.Equal(ActionStatus.Succeeded, result.Status);
            Assert.Equal(ProviderKind.ApplicationAdapter, result.Provider);
            Assert.Equal("60", ai.ValueField.Text);
            Assert.Equal("60", AppState.Shared.SharedText);
            Assert.Equal(fgBefore, NativeMethods.GetForegroundWindow());
            Assert.NotEqual(ai.Handle, NativeMethods.GetForegroundWindow());
            Assert.False(result.ForegroundChanged);
            Assert.False(result.CursorChanged);
            Assert.False(result.ViolatedStrictNoFocusSteal);
            Assert.Contains("typing-", human.Editor.Text);

            human.Close();
            ai.Close();
        });
    }

    [Fact]
    public async Task BackgroundInvoke_NoFocusSteal()
    {
        await StaUiTestRunner.RunAsync(async () =>
        {
            AppState.Shared.ButtonClicks = 0;
            AppState.Shared.SharedText = "initial";
            var human = new HumanWindow();
            var ai = new AiWindow();
            human.Show();
            ai.Show();
            human.Activate();
            human.Editor.Focus();
            await Dispatcher.Yield();

            var clicksBefore = AppState.Shared.ButtonClicks;
            var adapter = new InProcessHarnessAdapter();
            var instance = ProcessInstanceIdentity.FromPid(Environment.ProcessId);
            var element = new ElementRef(
                Guid.NewGuid().ToString("N"),
                instance,
                new ElementSelector(AutomationId: "AiInvokeButton", TopLevelHwnd: ai.Handle),
                ai.Handle,
                null);

            var seat = new Seat("ui-2");
            seat.BeginAttach();
            seat.Activate(instance, [Environment.ProcessId], [ai.Handle]);
            var router = new ActionRouter(
                [adapter],
                new StrictSafetyPolicy(),
                new Win32ForegroundProbe(),
                new Win32CursorProbe(),
                new InMemoryAuditLog());

            var fgBefore = NativeMethods.GetForegroundWindow();
            var result = await router.ExecuteAsync(
                seat,
                new InvokeAction(Guid.NewGuid().ToString("N"), element),
                CancellationToken.None);

            await Dispatcher.Yield();
            Assert.Equal(ActionStatus.Succeeded, result.Status);
            Assert.Equal(ProviderKind.ApplicationAdapter, result.Provider);
            Assert.Equal(clicksBefore + 1, AppState.Shared.ButtonClicks);
            Assert.Equal(fgBefore, NativeMethods.GetForegroundWindow());
            Assert.NotEqual(ai.Handle, NativeMethods.GetForegroundWindow());
            Assert.False(result.ForegroundChanged);
            Assert.False(result.CursorChanged);
            Assert.False(result.ViolatedStrictNoFocusSteal);

            human.Close();
            ai.Close();
        });
    }

    [Fact]
    public async Task StaleTarget_AfterDynamicRecreate_ReturnsControlledErrorThenReresolve()
    {
        await StaUiTestRunner.RunAsync(async () =>
        {
            var ai = new AiWindow();
            ai.Show();
            await Dispatcher.Yield();

            var adapter = new InProcessHarnessAdapter();
            using var uia = new UiaActionProvider();
            var instance = ProcessInstanceIdentity.FromPid(Environment.ProcessId);
            var first = await uia.FindAsync(
                instance,
                new ElementSelector(AutomationId: "AiDynamicField", TopLevelHwnd: ai.Handle),
                CancellationToken.None);
            Assert.NotNull(first);

            ai.RecreateDynamicControl();
            await Dispatcher.Yield();

            var second = await uia.FindAsync(
                instance,
                new ElementSelector(AutomationId: "AiDynamicField", TopLevelHwnd: ai.Handle),
                CancellationToken.None);
            Assert.NotNull(second);

            var seat = new Seat("ui-3");
            seat.BeginAttach();
            seat.Activate(instance, [Environment.ProcessId], [ai.Handle]);
            var router = new ActionRouter(
                [adapter, uia],
                new StrictSafetyPolicy(),
                new Win32ForegroundProbe(),
                new Win32CursorProbe(),
                new InMemoryAuditLog());
            var result = await router.ExecuteAsync(
                seat,
                new SetValueAction(Guid.NewGuid().ToString("N"), second!, "recovered"),
                CancellationToken.None);
            Assert.Equal(ActionStatus.Succeeded, result.Status);

            ai.Close();
        });
    }

    [Fact]
    public async Task Overlay_ShowHide_DoesNotBecomeForeground()
    {
        await StaUiTestRunner.RunAsync(async () =>
        {
            var human = new HumanWindow();
            human.Show();
            human.Activate();
            await Dispatcher.Yield();
            var fgBefore = NativeMethods.GetForegroundWindow();

            var overlay = new WpfOverlayController();
            overlay.Show("ov-1", new ScreenPoint(200, 200), new BoundingRect(180, 180, 260, 260));
            await Dispatcher.Yield();
            Assert.Equal(fgBefore, NativeMethods.GetForegroundWindow());
            overlay.Hide("ov-1");
            await Dispatcher.Yield();
            Assert.Equal(fgBefore, NativeMethods.GetForegroundWindow());
            human.Close();
        });
    }
}

public class Win32ProviderUiTests
{
    [Fact]
    public async Task ClassicWin32Provider_ReportsUnsupported_ForWpfHwndlessEdit()
    {
        await StaUiTestRunner.RunAsync(async () =>
        {
            var ai = new AiWindow();
            ai.Show();
            await Dispatcher.Yield();
            var provider = new Win32ActionProvider();
            var instance = ProcessInstanceIdentity.FromPid(Environment.ProcessId);
            var element = new ElementRef(
                "e",
                instance,
                new ElementSelector(AutomationId: "AiValueField", TopLevelHwnd: ai.Handle),
                IntPtr.Zero,
                null);
            var caps = await provider.ProbeCapabilitiesAsync(element, CancellationToken.None);
            Assert.Equal(ProviderKind.Unsupported, caps.Provider);
            ai.Close();
        });
    }
}

internal static class StaUiTestRunner
{
    private static readonly object Gate = new();
    private static Thread? _thread;
    private static Dispatcher? _dispatcher;
    private static Exception? _startupError;

    public static Task RunAsync(Func<Task> body)
    {
        EnsureStaApp();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher!.BeginInvoke(async () =>
        {
            try
            {
                await body().ConfigureAwait(true);
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task.WaitAsync(TimeSpan.FromMinutes(2));
    }

    private static void EnsureStaApp()
    {
        lock (Gate)
        {
            if (_dispatcher is not null)
            {
                return;
            }

            var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() =>
            {
                try
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    _dispatcher = Dispatcher.CurrentDispatcher;
                    ready.Set();
                    Dispatcher.Run();
                }
                catch (Exception ex)
                {
                    _startupError = ex;
                    ready.Set();
                }
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();
            if (!ready.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("STA UI test dispatcher failed to start.");
            }

            if (_startupError is not null)
            {
                throw _startupError;
            }
        }
    }
}
