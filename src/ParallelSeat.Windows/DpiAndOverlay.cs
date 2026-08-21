using ParallelSeat.Core;

namespace ParallelSeat.Windows;

public static class DpiCoordinateConverter
{
    public static ScreenPoint DipToScreen(double dipX, double dipY, double dpiScaleX, double dpiScaleY) =>
        new((int)Math.Round(dipX * dpiScaleX), (int)Math.Round(dipY * dpiScaleY));

    public static (double X, double Y) ScreenToDip(ScreenPoint point, double dpiScaleX, double dpiScaleY) =>
        (point.X / dpiScaleX, point.Y / dpiScaleY);

    public static BoundingRect ScaleRect(BoundingRect rect, double scaleX, double scaleY) =>
        new(
            (int)Math.Round(rect.Left * scaleX),
            (int)Math.Round(rect.Top * scaleY),
            (int)Math.Round(rect.Right * scaleX),
            (int)Math.Round(rect.Bottom * scaleY));
}

public static class OverlayWindowStyles
{
    public static void ApplyNonActivatingClickThrough(IntPtr hwnd)
    {
        var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_NOACTIVATE |
              NativeMethods.WS_EX_TOOLWINDOW |
              NativeMethods.WS_EX_LAYERED |
              NativeMethods.WS_EX_TRANSPARENT |
              NativeMethods.WS_EX_TOPMOST;
        _ = NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));
        _ = NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, NativeMethods.LWA_ALPHA);
        _ = NativeMethods.SetWindowPos(
            hwnd,
            new IntPtr(NativeMethods.HWND_TOPMOST),
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }
}
