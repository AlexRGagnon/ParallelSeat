using System.Threading;
using System.Windows;
using ParallelSeat.Ipc;

namespace ParallelSeat.Host;

public partial class App : Application
{
    public static ParallelSeatRuntime Runtime { get; private set; } = null!;

    private Mutex? _instanceMutex;
    private bool _headless;

    protected override void OnStartup(StartupEventArgs e)
    {
        _headless = HostLaunchOptions.IsHeadless(e.Args);
        if (_headless)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        _instanceMutex = new Mutex(initiallyOwned: true, HostLaunchOptions.GetMutexName(), out var createdNew);
        if (!createdNew)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown(HostLaunchOptions.ExitAlreadyRunning);
            return;
        }

        base.OnStartup(e);
        Runtime = new ParallelSeatRuntime(enableOverlay: !_headless);
        Runtime.ShutdownRequested += OnShutdownRequested;
        Runtime.StartIpc();

        if (!_headless)
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
    }

    private void OnShutdownRequested()
    {
        Dispatcher.BeginInvoke(() => Shutdown(0));
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (Runtime is not null)
        {
            Runtime.ShutdownRequested -= OnShutdownRequested;
            await Runtime.DisposeAsync().ConfigureAwait(true);
        }

        if (_instanceMutex is not null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch
            {
                // ignored
            }

            _instanceMutex.Dispose();
            _instanceMutex = null;
        }

        base.OnExit(e);
    }
}
