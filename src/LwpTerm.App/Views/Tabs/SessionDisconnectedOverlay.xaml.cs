using System.Windows;
using System.Windows.Controls;

namespace LwpTerm.App.Views.Tabs;

/// <summary>
/// MobaXterm-style overlay shown over a session surface once the transport has
/// dropped or failed: a message plus "Reconnect" / "Quit". Bind its DataContext
/// to a <see cref="ViewModels.Tabs.SessionTabViewModel"/>. Views can supply an
/// extra protocol-specific action via <see cref="ExtraContent"/>.
/// </summary>
public partial class SessionDisconnectedOverlay : UserControl
{
    public static readonly DependencyProperty ExtraContentProperty = DependencyProperty.Register(
        nameof(ExtraContent), typeof(object), typeof(SessionDisconnectedOverlay), new PropertyMetadata(null));

    public SessionDisconnectedOverlay() => InitializeComponent();

    public object? ExtraContent
    {
        get => GetValue(ExtraContentProperty);
        set => SetValue(ExtraContentProperty, value);
    }
}
