using System.Security.Principal;

namespace ParallelSeat.Ipc;

/// <summary>
/// Shared Host launch helpers (arg parse + single-instance mutex name).
/// </summary>
public static class HostLaunchOptions
{
    public const int ExitAlreadyRunning = 2;

    public static bool IsHeadless(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            if (arg.Equals("--headless", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--agent", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("/headless", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("/agent", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string GetMutexName()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? "unknown";
        return $"Local\\ParallelSeat.Host.{sid}";
    }
}
