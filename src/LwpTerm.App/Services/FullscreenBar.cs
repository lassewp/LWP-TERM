using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace LwpTerm.App.Services;

/// <summary>
/// The mstsc-style auto-hiding bar shown while a session is full screen. It
/// slides down when the pointer touches the top edge of the screen and carries
/// the session title plus restore / mode / pin / minimise actions.
///
/// It is a separate top-most layered window on purpose: RDP / VNC / WebView2
/// surfaces are child HWNDs that paint over any in-tree WPF overlay (airspace),
/// and they can also capture the keyboard, so a mouse-reachable way out has to
/// live in its own window above them.
/// </summary>
internal sealed class FullscreenBar : Window
{
    private const double BarHeight = 34;

    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _autoHide = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly TextBlock _title;
    private readonly ToggleButton _pin;
    private readonly Button _modeButton;

    private Rect _rectDip;
    private (int Left, int Top, int Right, int Bottom) _rectPx;
    private bool _revealed;

    public event Action? ExitRequested;
    public event Action? ToggleModeRequested;
    public event Action? MinimiseRequested;

    public FullscreenBar(Window owner)
    {
        Owner = owner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        Height = BarHeight;
        Visibility = Visibility.Hidden;

        _title = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(16, 0, 12, 0),
        };

        _modeButton = MakeButton("Borderless", () => ToggleModeRequested?.Invoke());
        _pin = new ToggleButton
        {
            Content = "Keep bar",
            Focusable = false,
            Margin = new Thickness(3, 5, 3, 5),
            Padding = new Thickness(10, 2, 10, 2),
            ToolTip = "Keep this bar visible",
        };
        _pin.Checked += (_, _) => UpdateAutoHide();
        _pin.Unchecked += (_, _) => UpdateAutoHide();
        var minButton = MakeButton("Minimise", () => MinimiseRequested?.Invoke());
        var exitButton = MakeButton("Exit full screen", () => ExitRequested?.Invoke());

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Children = { _modeButton, _pin, minButton, exitButton },
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        Grid.SetColumn(_title, 0);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(_title);
        grid.Children.Add(actions);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1E, 0x1E, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid,
        };

        _poll.Tick += (_, _) => PollCursor();
        _autoHide.Tick += (_, _) =>
        {
            _autoHide.Stop();
            if (!IsPinned && !IsMouseOver)
            {
                Conceal();
            }
        };
        MouseEnter += (_, _) => _autoHide.Stop();
        MouseLeave += (_, _) => UpdateAutoHide();
    }

    private bool IsPinned => _pin.IsChecked == true;

    /// <summary>Show the bar, then let it auto-hide. <paramref name="rectPx"/> is the
    /// full-screen window's rectangle in physical pixels (for cursor hit-testing).</summary>
    public void Begin(string title, Rect rectDip, (int, int, int, int) rectPx, string modeActionLabel)
    {
        _title.Text = title;
        _rectDip = rectDip;
        _rectPx = rectPx;
        _modeButton.Content = modeActionLabel;

        Width = Math.Min(780, rectDip.Width);
        Left = rectDip.Left + ((rectDip.Width - Width) / 2);

        Reveal();
        _poll.Start();
        UpdateAutoHide();
    }

    public void End()
    {
        _poll.Stop();
        _autoHide.Stop();
        Visibility = Visibility.Hidden;
        _pin.IsChecked = false;
    }

    public void SetModeActionLabel(string label) => _modeButton.Content = label;

    private void PollCursor()
    {
        if (!GetCursorPos(out var p))
        {
            return;
        }

        var insideX = p.X >= _rectPx.Left && p.X < _rectPx.Right;
        var atTopEdge = insideX && p.Y <= _rectPx.Top + 2;
        var wellBelow = p.Y > _rectPx.Top + 140;

        if (atTopEdge && !_revealed)
        {
            Reveal();
        }
        else if (_revealed && wellBelow && !IsPinned && !IsMouseOver)
        {
            Conceal();
        }
    }

    private void Reveal()
    {
        _revealed = true;
        Top = _rectDip.Top;
        Visibility = Visibility.Visible;
        UpdateAutoHide();
    }

    private void Conceal()
    {
        _revealed = false;
        Visibility = Visibility.Hidden;
        Top = _rectDip.Top - BarHeight - 4;
    }

    private void UpdateAutoHide()
    {
        _autoHide.Stop();
        if (_revealed && !IsPinned && !IsMouseOver)
        {
            _autoHide.Start();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _poll.Stop();
        _autoHide.Stop();
        base.OnClosed(e);
    }

    private static Button MakeButton(string text, Action onClick)
    {
        var b = new Button
        {
            Content = text,
            Focusable = false,
            Margin = new Thickness(3, 5, 3, 5),
            Padding = new Thickness(12, 2, 12, 2),
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
