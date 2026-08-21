using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using ParallelSeat.Core;
using ParallelSeat.Windows;

namespace ParallelSeat.Overlay;

public sealed class AiCursorOverlayWindow : Window
{
    private readonly Ellipse _cursorDot;
    private readonly TextBlock _label;
    private readonly Rectangle _outline;
    private readonly TextBlock _feedback;

    public AiCursorOverlayWindow()
    {
        Title = "ParallelSeat AI Overlay";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        IsHitTestVisible = false;
        Focusable = false;
        ShowActivated = false;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;

        var canvas = new Canvas();
        _cursorDot = new Ellipse
        {
            Width = 14,
            Height = 14,
            Fill = new SolidColorBrush(Color.FromArgb(200, 0, 140, 255)),
            Stroke = Brushes.White,
            StrokeThickness = 2
        };
        _label = new TextBlock
        {
            Text = "AI",
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(180, 0, 90, 180)),
            Padding = new Thickness(4, 1, 4, 1),
            FontWeight = FontWeights.Bold
        };
        _outline = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromArgb(200, 0, 140, 255)),
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            Visibility = Visibility.Collapsed
        };
        _feedback = new TextBlock
        {
            Foreground = Brushes.OrangeRed,
            Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            Padding = new Thickness(6, 2, 6, 2),
            Visibility = Visibility.Collapsed
        };

        canvas.Children.Add(_outline);
        canvas.Children.Add(_cursorDot);
        canvas.Children.Add(_label);
        canvas.Children.Add(_feedback);
        Content = canvas;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            OverlayWindowStyles.ApplyNonActivatingClickThrough(hwnd);
        };
    }

    public void Render(ScreenPoint logicalCursor, BoundingRect? targetOutline, string label, string feedback)
    {
        var originX = SystemParameters.VirtualScreenLeft;
        var originY = SystemParameters.VirtualScreenTop;
        Canvas.SetLeft(_cursorDot, logicalCursor.X - originX - 7);
        Canvas.SetTop(_cursorDot, logicalCursor.Y - originY - 7);
        Canvas.SetLeft(_label, logicalCursor.X - originX + 10);
        Canvas.SetTop(_label, logicalCursor.Y - originY - 18);
        _label.Text = label;

        if (targetOutline is null)
        {
            _outline.Visibility = Visibility.Collapsed;
        }
        else
        {
            _outline.Visibility = Visibility.Visible;
            Canvas.SetLeft(_outline, targetOutline.Left - originX);
            Canvas.SetTop(_outline, targetOutline.Top - originY);
            _outline.Width = Math.Max(1, targetOutline.Right - targetOutline.Left);
            _outline.Height = Math.Max(1, targetOutline.Bottom - targetOutline.Top);
        }

        if (string.IsNullOrWhiteSpace(feedback))
        {
            _feedback.Visibility = Visibility.Collapsed;
        }
        else
        {
            _feedback.Text = feedback;
            _feedback.Visibility = Visibility.Visible;
            Canvas.SetLeft(_feedback, logicalCursor.X - originX + 10);
            Canvas.SetTop(_feedback, logicalCursor.Y - originY + 10);
        }
    }
}

public sealed class WpfOverlayController : IOverlayController
{
    private readonly Dictionary<string, AiCursorOverlayWindow> _windows = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly bool _enabled;

    public WpfOverlayController(bool enabled = true) => _enabled = enabled;

    public void Show(string seatId, ScreenPoint logicalCursor, BoundingRect? targetOutline, string label = "AI")
    {
        if (!_enabled)
        {
            return;
        }

        RunOnSta(() =>
        {
            var window = GetOrCreate(seatId);
            window.Render(logicalCursor, targetOutline, label, "");
            if (!window.IsVisible)
            {
                window.Show();
            }
        });
    }

    public void Update(string seatId, ScreenPoint logicalCursor, BoundingRect? targetOutline, string feedback = "")
    {
        if (!_enabled)
        {
            return;
        }

        RunOnSta(() =>
        {
            if (_windows.TryGetValue(seatId, out var window))
            {
                window.Render(logicalCursor, targetOutline, "AI", feedback);
            }
        });
    }

    public void Hide(string seatId)
    {
        if (!_enabled)
        {
            return;
        }

        RunOnSta(() =>
        {
            if (_windows.TryGetValue(seatId, out var window))
            {
                window.Hide();
                window.Close();
                _windows.Remove(seatId);
            }
        });
    }

    public void HideAll()
    {
        if (!_enabled)
        {
            return;
        }

        RunOnSta(() =>
        {
            foreach (var window in _windows.Values.ToList())
            {
                window.Hide();
                window.Close();
            }

            _windows.Clear();
        });
    }

    private AiCursorOverlayWindow GetOrCreate(string seatId)
    {
        lock (_gate)
        {
            if (_windows.TryGetValue(seatId, out var existing))
            {
                return existing;
            }

            var created = new AiCursorOverlayWindow();
            _windows[seatId] = created;
            return created;
        }
    }

    private static void RunOnSta(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }
}
