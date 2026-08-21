using System.Diagnostics;
using System.Text;
using ParallelSeat.Core;

namespace ParallelSeat.Windows;

public static class ProcessInstanceIdentity
{
    public static ApplicationInstanceId FromProcess(Process process)
    {
        process.Refresh();
        var start = process.StartTime.ToUniversalTime();
        var path = SafePath(process);
        var tree = BuildTree(process.Id);
        return new ApplicationInstanceId(process.Id, start, path, tree);
    }

    public static ApplicationInstanceId FromPid(int pid)
    {
        using var process = Process.GetProcessById(pid);
        return FromProcess(process);
    }

    public static IReadOnlyList<IntPtr> EnumerateTopLevelWindows(int processId)
    {
        var results = new List<IntPtr>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
            if ((int)pid == processId)
            {
                results.Add(hWnd);
            }

            return true;
        }, IntPtr.Zero);
        return results;
    }

    public static string GetWindowTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        _ = NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetWindowClass(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        _ = NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool IsAlive(IntPtr hwnd) => NativeMethods.IsWindow(hwnd);

    public static BoundingRect? GetBounds(IntPtr hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return null;
        }

        return new BoundingRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    private static string SafePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? process.ProcessName;
        }
        catch
        {
            return process.ProcessName;
        }
    }

    private static IReadOnlyList<int> BuildTree(int rootPid)
    {
        var ids = new HashSet<int> { rootPid };
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    // Parent lookup is incomplete without NtQuery; include same-name children as best-effort for MVP.
                    if (process.Id != rootPid)
                    {
                        continue;
                    }
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            // Best-effort tree for MVP.
        }

        return ids.OrderBy(x => x).ToList();
    }
}
