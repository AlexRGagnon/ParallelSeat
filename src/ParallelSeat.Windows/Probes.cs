using ParallelSeat.Core;

namespace ParallelSeat.Windows;

public sealed class Win32ForegroundProbe : IForegroundProbe
{
    public IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();
}

public sealed class Win32CursorProbe : ICursorProbe
{
    public ScreenPoint GetCursorPosition()
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            return new ScreenPoint(0, 0);
        }

        return new ScreenPoint(point.X, point.Y);
    }
}
