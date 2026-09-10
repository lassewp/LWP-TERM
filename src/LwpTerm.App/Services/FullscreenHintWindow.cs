using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace LwpTerm.App.Services;

/// <summary>
/// A small translucent "press Esc to exit" pill shown briefly when a session
/// goes full screen. It is a separate top-most window on purpose: RDP / VNC /
/// WebView2 surfaces are child HWNDs that paint over any in-tree WPF overlay
/// (airspace), so the reminder has to live in its own layered window to be
/// visible above them.
/// </summary>
internal sealed class FullscreenHintWindow : Window
{
    private readonly DispatcherTimer _hideTimer;
    private Rect _target;

    public FullscreenHintWindow(Window owner, Action onExit)
    {
        Owner = owner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;

        var text = new TextBlock
        {
            Text = "Full screen",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White,
            FontSize = 13,
        };

        var exit = new Button
        {
            Content = "Exit  (Esc)",
            Margin = new Thickness(12, 0, 0, 0),
            Padding = new Thickness(10, 3, 10, 3),
            Focusable = false,
        };
        exit.Click += (_, _) => onExit();

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x1E, 0x1E, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 8, 10, 8),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { text, exit },
            },
        };

        SizeChanged += (_, _) => Reposition();

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    /// <summary>Show the pill centred on <paramref name="monitorDip"/> and fade it after a few seconds.</summary>
    public void Flash(Rect monitorDip)
    {
        _target = monitorDip;
        Show();
        Reposition();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void Reposition()
    {
        if (_target.Width <= 0 || ActualWidth <= 0)
        {
            return;
        }

        Left = _target.Left + ((_target.Width - ActualWidth) / 2);
        Top = _target.Top + 12;
    }
}
