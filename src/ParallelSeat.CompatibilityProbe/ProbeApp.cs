using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ParallelSeat.Adapters.Abstractions;
using ParallelSeat.Windows;

namespace ParallelSeat.CompatibilityProbe;

public sealed class ProbeWindow : Window
{
    private readonly TextBox _pidBox;
    private readonly TextBox _report;

    public ProbeWindow()
    {
        Title = "ParallelSeat Compatibility Probe";
        Width = 720;
        Height = 560;
        var panel = new DockPanel { Margin = new Thickness(12) };
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(top, Dock.Top);
        top.Children.Add(new TextBlock { Text = "PID:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        _pidBox = new TextBox { Width = 120 };
        top.Children.Add(_pidBox);
        var run = new Button { Content = "Probe", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 4, 12, 4) };
        run.Click += (_, _) => RunProbe();
        top.Children.Add(run);
        _report = new TextBox { FontFamily = new System.Windows.Media.FontFamily("Consolas"), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(top);
        panel.Children.Add(_report);
        Content = panel;
    }

    private void RunProbe()
    {
        if (!int.TryParse(_pidBox.Text, out var pid))
        {
            _report.Text = "Enter a valid PID.";
            return;
        }

        try
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

            _report.Text = sb.ToString();
        }
        catch (Exception ex)
        {
            _report.Text = ex.ToString();
        }
    }
}

public static class CompatibilityInspector
{
    public static CompatibilityReport Inspect(int processId)
    {
        using var process = Process.GetProcessById(processId);
        var path = process.MainModule?.FileName ?? process.ProcessName;
        var hwnds = ProcessInstanceIdentity.EnumerateTopLevelWindows(processId);
        var notes = new List<string>();
        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasStableIds = false;
        var windowlessHints = 0;
        var windowed = 0;

        foreach (var hwnd in hwnds.Take(8))
        {
            try
            {
                var root = AutomationElement.FromHandle(hwnd);
                frameworks.Add(root.Current.FrameworkId ?? "Unknown");
                Walk(root, 0, patterns, ref hasStableIds, ref windowlessHints, ref windowed);
            }
            catch (Exception ex)
            {
                notes.Add($"HWND 0x{hwnd.ToInt64():X}: {ex.Message}");
            }
        }

        var tier = Classify(patterns, hasStableIds, windowlessHints, windowed, frameworks);
        notes.Add($"Top-level windows: {hwnds.Count}");
        notes.Add(hasStableIds ? "Stable AutomationIds observed." : "Few/no AutomationIds observed.");
        if (windowlessHints > windowed)
        {
            notes.Add("Many elements lack native HWNDs (windowless).");
        }

        return new CompatibilityReport(
            path,
            processId,
            tier,
            string.Join(", ", frameworks),
            patterns.OrderBy(x => x).ToList(),
            notes,
            RequiresForeground: false,
            IsElevated: IsElevated(process),
            IsProtected: false);
    }

    private static CompatibilityTier Classify(
        HashSet<string> patterns,
        bool hasStableIds,
        int windowless,
        int windowed,
        HashSet<string> frameworks)
    {
        if (patterns.Count == 0)
        {
            return CompatibilityTier.F_Unsupported;
        }

        var semantic = patterns.Contains("Invoke") || patterns.Contains("Value") || patterns.Contains("Toggle");
        if (semantic && hasStableIds && frameworks.Any(f => f is "WPF" or "WinForm" or "Win32"))
        {
            return CompatibilityTier.B_FullUiAutomation;
        }

        if (semantic || windowed > 0)
        {
            return CompatibilityTier.C_PartialUiaPlusWin32;
        }

        if (windowless > 0)
        {
            return CompatibilityTier.D_RequiresProcessBridge;
        }

        return CompatibilityTier.E_FocusStealOnly;
    }

    private static void Walk(
        AutomationElement element,
        int depth,
        HashSet<string> patterns,
        ref bool hasStableIds,
        ref int windowless,
        ref int windowed)
    {
        if (depth > 6)
        {
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(element.Current.AutomationId))
            {
                hasStableIds = true;
            }

            if (element.Current.NativeWindowHandle == 0)
            {
                windowless++;
            }
            else
            {
                windowed++;
            }

            foreach (var pattern in element.GetSupportedPatterns())
            {
                patterns.Add(pattern.ProgrammaticName.Replace("PatternIdentifiers.Pattern", "").Replace("Pattern", ""));
            }

                var children = element.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
            foreach (AutomationElement child in children)
            {
                Walk(child, depth + 1, patterns, ref hasStableIds, ref windowless, ref windowed);
            }
        }
        catch
        {
            // ignore individual node failures
        }
    }

    private static bool IsElevated(Process process)
    {
        try
        {
            _ = process.MainModule;
            return false;
        }
        catch
        {
            return true;
        }
    }
}
