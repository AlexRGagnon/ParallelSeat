using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace ParallelSeat.Host;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
        PreviewKeyDown += OnPreviewKeyDown;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Refresh();
        timer.Start();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F9 && Keyboard.Modifiers == ModifierKeys.Control)
        {
            App.Runtime.Seats.EmergencyStop();
            Refresh();
            e.Handled = true;
        }
    }

    private void EmergencyStopButton_Click(object sender, RoutedEventArgs e)
    {
        App.Runtime.Seats.EmergencyStop();
        Refresh();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        var runtime = App.Runtime;
        DiagnosticsText.Text =
            $"Pipe: {runtime.PipeName}\n" +
            $"Token: {runtime.Token}\n" +
            $"Seats: {runtime.Seats.List().Count}\n" +
            $"Audit entries: {runtime.Audit.Snapshot().Count}\n" +
            string.Join("\n", runtime.Seats.List().Select(s => $" - {s.SeatId}: {s.State} conflict={s.ConflictState}"));
    }
}
