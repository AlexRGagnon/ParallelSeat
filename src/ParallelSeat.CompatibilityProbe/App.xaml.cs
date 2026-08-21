using System.IO;
using System.Text;
using System.Windows;

namespace ParallelSeat.CompatibilityProbe;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = e.Args;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--pid", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out var pid))
            {
                WriteHeadlessReport(pid);
                Shutdown(0);
                return;
            }
        }

        var window = new ProbeWindow();
        MainWindow = window;
        window.Show();
    }

    private static void WriteHeadlessReport(int pid)
    {
        var report = CompatibilityInspector.Inspect(pid);
        var sb = new StringBuilder();
        sb.AppendLine($"Executable: {report.ExecutablePath}");
        sb.AppendLine($"PID: {report.ProcessId}");
        sb.AppendLine($"Tier: {report.Tier}");
        sb.AppendLine($"Framework: {report.FrameworkSummary}");
        sb.AppendLine($"RequiresForeground: {report.RequiresForeground}");
        sb.AppendLine($"Elevated: {report.IsElevated}");
        sb.AppendLine($"Protected: {report.IsProtected}");
        sb.AppendLine("Patterns:");
        foreach (var p in report.AvailablePatterns)
        {
            sb.AppendLine($"  - {p}");
        }

        sb.AppendLine("Notes:");
        foreach (var n in report.Notes)
        {
            sb.AppendLine($"  - {n}");
        }

        var text = sb.ToString();
        var repoDocs = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "compatibility"));
        Directory.CreateDirectory(repoDocs);
        var file = System.IO.Path.Combine(repoDocs, $"probe-{Sanitize(report.ExecutablePath)}-{pid}.txt");
        File.WriteAllText(file, text);
        File.WriteAllText(System.IO.Path.Combine(repoDocs, "probe-latest.txt"), text);
    }

    private static string Sanitize(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "app" : name;
    }
}
