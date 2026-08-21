using System.Windows;

namespace ParallelSeat.TestHarness;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var human = new HumanWindow();
        var ai = new AiWindow();
        var instrumentation = new InstrumentationWindow(human, ai);

        human.Show();
        ai.Show();
        instrumentation.Show();
    }
}
