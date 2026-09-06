using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LwpTerm.App.ViewModels;

namespace LwpTerm.App.Views.Panels;

public partial class SessionTreeView : UserControl
{
    public SessionTreeView()
    {
        InitializeComponent();
        Tree.SelectedItemChanged += OnSelectedItemChanged;
    }

    private void OnSelectedItemChanged(object? sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is SessionTreeViewModel vm)
        {
            vm.SelectedNode = e.NewValue as SessionNodeViewModel;
        }
    }

    private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not SessionTreeViewModel vm)
        {
            return;
        }

        // Only react when the click landed on a tree item, not empty space.
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(Tree, source) is TreeViewItem { DataContext: SessionNodeViewModel node })
        {
            if (vm.ConnectCommand.CanExecute(node))
            {
                vm.ConnectCommand.Execute(node);
            }

            e.Handled = true;
        }
    }
}
