using System.IO;
using System.Text;
using System.Windows.Automation;
using ParallelSeat.Automation;
using ParallelSeat.Core;
using ParallelSeat.Windows;

namespace ParallelSeat.NotepadExperiment;

/// <summary>
/// Experiments 1–2 against a live Windows Notepad instance.
/// Does not activate Notepad intentionally; measures FG HWND + cursor around actions.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var pid = ParsePid(args) ?? FindNotepadPid();
        if (pid is null)
        {
            Console.Error.WriteLine("Notepad not found. Pass --pid <id>.");
            return 2;
        }

        Console.WriteLine($"Target Notepad PID={pid.Value}");
        var instance = ProcessInstanceIdentity.FromPid(pid.Value);
        var hwnds = ProcessInstanceIdentity.EnumerateTopLevelWindows(pid.Value);
        if (hwnds.Count == 0)
        {
            Console.Error.WriteLine("No top-level windows for Notepad.");
            return 3;
        }

        var top = hwnds[0];
        Console.WriteLine($"Top HWND=0x{top.ToInt64():X} title={ProcessInstanceIdentity.GetWindowTitle(top)}");

        using var uia = new UiaActionProvider();
        var audit = new InMemoryAuditLog();
        var router = new ActionRouter(
            [uia],
            new StrictSafetyPolicy(),
            new Win32ForegroundProbe(),
            new Win32CursorProbe(),
            audit);

        var seat = new Seat("notepad-exp", SafetyMode.Strict);
        seat.BeginAttach();
        seat.Activate(instance, instance.ProcessTreeIds, hwnds);

        var doc = await FindDocumentAsync(uia, instance, top).ConfigureAwait(false);
        if (doc is null)
        {
            Console.Error.WriteLine("Could not resolve Document/Edit element.");
            return 4;
        }

        Console.WriteLine($"Resolved document elementId={doc.ElementId} hwnd={doc.NativeHwnd}");

        var report = new StringBuilder();
        report.AppendLine("# Notepad Experiments 1–2");
        report.AppendLine($"TimestampUtc: {DateTime.UtcNow:o}");
        report.AppendLine($"PID: {pid.Value}");
        report.AppendLine($"Instance: {instance.Value}");
        report.AppendLine($"TopHwnd: 0x{top.ToInt64():X}");
        report.AppendLine();

        // --- Experiment 1: SetValue ---
        var exp1 = await RunSetValueAsync(router, seat, doc, uia).ConfigureAwait(false);
        report.AppendLine("## Experiment 1 — Background SetValue");
        report.AppendLine(exp1.ToReport());
        report.AppendLine();

        // Fresh seat for Exp2 so Exp1 pause/fault cannot contaminate results.
        if (seat.State is SeatState.Paused or SeatState.Faulted)
        {
            seat.BeginStop();
            seat.Destroy();
            seat = new Seat("notepad-exp-2", SafetyMode.Strict);
            seat.BeginAttach();
            seat.Activate(instance, instance.ProcessTreeIds, hwnds);
        }

        // --- Experiment 2: Invoke (prefer a non-menu button; fall back to first Invoke-capable child) ---
        var invokable = await FindInvokableAsync(uia, instance, top).ConfigureAwait(false);
        ExperimentOutcome exp2;
        if (invokable is null)
        {
            exp2 = ExperimentOutcome.Skip("No safe Invoke target found (avoided menus).");
        }
        else
        {
            exp2 = await RunInvokeAsync(router, seat, invokable).ConfigureAwait(false);
        }

        report.AppendLine("## Experiment 2 — Background Invoke");
        report.AppendLine(exp2.ToReport());
        report.AppendLine();

        report.AppendLine("## Verdict");
        report.AppendLine(Summarize(exp1, exp2));

        var text = report.ToString();
        Console.WriteLine(text);

        var outDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "testing"));
        Directory.CreateDirectory(outDir);
        var outFile = System.IO.Path.Combine(outDir, "notepad-experiments-1-2.md");
        File.WriteAllText(outFile, text);
        Console.WriteLine($"Wrote {outFile}");

        seat.BeginStop();
        seat.Destroy();
        return exp1.Pass || exp2.Pass ? 0 : 1;
    }

    private static string Summarize(ExperimentOutcome exp1, ExperimentOutcome exp2)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Exp1 SetValue: {(exp1.Pass ? "PASS" : exp1.Skipped ? "SKIP" : "FAIL")} — {exp1.Detail}");
        sb.AppendLine($"Exp2 Invoke: {(exp2.Pass ? "PASS" : exp2.Skipped ? "SKIP" : "FAIL")} — {exp2.Detail}");
        if (exp1.Pass)
        {
            sb.AppendLine("Architectural consequence: Tier B UIA SetValue can operate without FG/cursor change on this Notepad build.");
        }
        else if (!exp1.Skipped && exp1.FocusStealDetected)
        {
            sb.AppendLine("Architectural consequence: Strict mode correctly rejected focus/cursor theft; Notepad SetValue needs adapter or Tier E for that action.");
        }
        else if (!exp1.Skipped)
        {
            sb.AppendLine("Architectural consequence: SetValue unsupported or failed via UIA ValuePattern; may need TextPattern support or different selector.");
        }

        return sb.ToString().TrimEnd();
    }

    private static async Task<ExperimentOutcome> RunSetValueAsync(
        ActionRouter router,
        Seat seat,
        ElementRef doc,
        UiaActionProvider uia)
    {
        var caps = await uia.ProbeCapabilitiesAsync(doc, CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"Document capabilities: {caps.Flags} ({caps.Notes})");

        var marker = $"PS-{DateTime.UtcNow:HHmmss}";
        var result = await router.ExecuteAsync(
            seat,
            new SetValueAction(Guid.NewGuid().ToString("N"), doc, marker),
            CancellationToken.None).ConfigureAwait(false);

        // Fallback: TextPattern replace if ValuePattern unavailable
        if (result.Status == ActionStatus.Unsupported)
        {
            Console.WriteLine("ValuePattern unsupported; attempting TextPattern document replace...");
            var textResult = await TryTextPatternReplaceAsync(doc, marker).ConfigureAwait(false);
            return textResult;
        }

        return ExperimentOutcome.FromAction("SetValue", result, marker);
    }

    private static async Task<ExperimentOutcome> RunInvokeAsync(ActionRouter router, Seat seat, ElementRef target)
    {
        var result = await router.ExecuteAsync(
            seat,
            new InvokeAction(Guid.NewGuid().ToString("N"), target),
            CancellationToken.None).ConfigureAwait(false);
        return ExperimentOutcome.FromAction("Invoke", result, target.Selector.Name ?? target.Selector.AutomationId ?? target.ElementId);
    }

    private static async Task<ExperimentOutcome> TryTextPatternReplaceAsync(ElementRef doc, string marker)
    {
        var fgBefore = NativeMethods.GetForegroundWindow();
        NativeMethods.GetCursorPos(out var cursorBefore);
        var started = DateTimeOffset.UtcNow;
        string? error = null;
        try
        {
            await Task.Run(() =>
            {
                var ae = ElementResolver.Resolve(doc.Selector)
                         ?? throw new InvalidOperationException("Document unresolved for TextPattern.");
                if (!ae.TryGetCurrentPattern(TextPattern.Pattern, out var obj) || obj is not TextPattern text)
                {
                    throw new InvalidOperationException("TextPattern not available.");
                }

                var range = text.DocumentRange;
                range.Select();
                // TextPattern has no SetValue; use ValuePattern if present after select, else fail honestly.
                if (ae.TryGetCurrentPattern(ValuePattern.Pattern, out var vObj) && vObj is ValuePattern value && !value.Current.IsReadOnly)
                {
                    value.SetValue(marker);
                    return;
                }

                throw new InvalidOperationException("No writable ValuePattern after TextPattern resolve.");
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        var fgAfter = NativeMethods.GetForegroundWindow();
        NativeMethods.GetCursorPos(out var cursorAfter);
        var fgChanged = fgBefore != fgAfter;
        var cursorChanged = cursorBefore.X != cursorAfter.X || cursorBefore.Y != cursorAfter.Y;
        if (error is not null)
        {
            return new ExperimentOutcome(false, false, false, fgChanged, cursorChanged, error, DateTimeOffset.UtcNow - started);
        }

        if (fgChanged || cursorChanged)
        {
            return new ExperimentOutcome(false, false, true, fgChanged, cursorChanged,
                "Text/Value path changed FG or cursor.", DateTimeOffset.UtcNow - started);
        }

        return new ExperimentOutcome(true, false, false, false, false,
            $"Wrote marker '{marker}' without FG/cursor change.", DateTimeOffset.UtcNow - started);
    }

    private static async Task<ElementRef?> FindDocumentAsync(UiaActionProvider uia, ApplicationInstanceId instance, IntPtr top)
    {
        foreach (var selector in new[]
                 {
                     new ElementSelector(ControlType: "Document", TopLevelHwnd: top),
                     new ElementSelector(ControlType: "Edit", TopLevelHwnd: top),
                     new ElementSelector(AutomationId: "15", TopLevelHwnd: top), // classic edit id sometimes
                     new ElementSelector(ClassName: "RichEditD2DPT", TopLevelHwnd: top),
                     new ElementSelector(ClassName: "Edit", TopLevelHwnd: top)
                 })
        {
            try
            {
                var found = await uia.FindAsync(instance, selector, CancellationToken.None).ConfigureAwait(false);
                if (found is not null)
                {
                    return found;
                }
            }
            catch
            {
                // try next
            }
        }

        // Manual walk for Document/Edit
        return await Task.Run(() =>
        {
            var root = AutomationElement.FromHandle(top);
            var doc = root.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            doc ??= root.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            if (doc is null)
            {
                return null;
            }

            return new ElementRef(
                Guid.NewGuid().ToString("N"),
                instance,
                new ElementSelector(
                    AutomationId: doc.Current.AutomationId,
                    Name: doc.Current.Name,
                    ClassName: doc.Current.ClassName,
                    ControlType: doc.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""),
                    TopLevelHwnd: top,
                    ChildHwnd: new IntPtr(doc.Current.NativeWindowHandle)),
                new IntPtr(doc.Current.NativeWindowHandle),
                string.Join(".", doc.GetRuntimeId() ?? []));
        }).ConfigureAwait(false);
    }

    private static async Task<ElementRef?> FindInvokableAsync(UiaActionProvider uia, ApplicationInstanceId instance, IntPtr top)
    {
        return await Task.Run(() =>
        {
            var root = AutomationElement.FromHandle(top);
            var buttons = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            foreach (AutomationElement button in buttons)
            {
                try
                {
                    var name = button.Current.Name ?? "";
                    // Skip destructive / focus-heavy chrome
                    if (name.Contains("Close", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Minimize", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Maximize", StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    if (!button.TryGetCurrentPattern(InvokePattern.Pattern, out _))
                    {
                        continue;
                    }

                    // Prefer settings/see more style non-modal toolbar buttons
                    return new ElementRef(
                        Guid.NewGuid().ToString("N"),
                        instance,
                        new ElementSelector(
                            AutomationId: button.Current.AutomationId,
                            Name: button.Current.Name,
                            ControlType: "Button",
                            TopLevelHwnd: top),
                        new IntPtr(button.Current.NativeWindowHandle),
                        string.Join(".", button.GetRuntimeId() ?? []));
                }
                catch
                {
                    // continue
                }
            }

            return null;
        }).ConfigureAwait(false);
    }

    private static int? ParsePid(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--pid" && int.TryParse(args[i + 1], out var pid))
            {
                return pid;
            }
        }

        return null;
    }

    private static int? FindNotepadPid()
    {
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("Notepad"))
        {
            try
            {
                if (p.MainWindowHandle != IntPtr.Zero)
                {
                    return p.Id;
                }
            }
            finally
            {
                p.Dispose();
            }
        }

        return null;
    }
}

internal sealed record ExperimentOutcome(
    bool Pass,
    bool Skipped,
    bool FocusStealDetected,
    bool ForegroundChanged,
    bool CursorChanged,
    string Detail,
    TimeSpan Duration)
{
    public static ExperimentOutcome Skip(string detail) =>
        new(false, true, false, false, false, detail, TimeSpan.Zero);

    public static ExperimentOutcome FromAction(string name, ActionResult result, string context)
    {
        var steal = result.ViolatedStrictNoFocusSteal || result.Status == ActionStatus.RejectedBySafety;
        var pass = result.Status == ActionStatus.Succeeded && !result.ViolatedStrictNoFocusSteal;
        var detail =
            $"{name} status={result.Status} provider={result.Provider} context={context} " +
            $"fgChanged={result.ForegroundChanged} cursorChanged={result.CursorChanged} msg={result.Message}";
        return new ExperimentOutcome(
            pass,
            false,
            steal,
            result.ForegroundChanged,
            result.CursorChanged,
            detail,
            result.Duration);
    }

    public string ToReport() =>
        $"- Pass: {Pass}\n- Skipped: {Skipped}\n- FocusStealDetected: {FocusStealDetected}\n" +
        $"- ForegroundChanged: {ForegroundChanged}\n- CursorChanged: {CursorChanged}\n" +
        $"- Duration: {Duration.TotalMilliseconds:F0} ms\n- Detail: {Detail}";
}

internal static class AutomationElementExtensions
{
    public static bool TryGetCurrentPattern(this AutomationElement element, AutomationPattern pattern, out object? patternObject)
    {
        try
        {
            patternObject = element.GetCurrentPattern(pattern);
            return patternObject is not null;
        }
        catch (InvalidOperationException)
        {
            patternObject = null;
            return false;
        }
    }
}
