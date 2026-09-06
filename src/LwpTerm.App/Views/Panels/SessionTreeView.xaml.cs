using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LwpTerm.App.ViewModels;

namespace LwpTerm.App.Views.Panels;

public partial class SessionTreeView : UserControl
{
    private Point _dragStart;
    private SessionNodeViewModel? _dragCandidate;

    public SessionTreeView()
    {
        InitializeComponent();
        Tree.SelectedItemChanged += OnSelectedItemChanged;
    }

    private SessionTreeViewModel? Vm => DataContext as SessionTreeViewModel;

    private void OnSelectedItemChanged(object? sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (Vm is not null)
        {
            Vm.SelectedNode = e.NewValue as SessionNodeViewModel;
        }
    }

    private void OnItemPreviewRightDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeViewItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void OnTreePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && Vm is { } vm && vm.RenameCommand.CanExecute(vm.SelectedNode))
        {
            vm.RenameCommand.Execute(vm.SelectedNode);
            e.Handled = true;
        }
    }

    private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null)
        {
            return;
        }

        if (NodeUnder(e.OriginalSource as DependencyObject) is { IsFolder: false } node &&
            Vm.ConnectCommand.CanExecute(node))
        {
            Vm.ConnectCommand.Execute(node);
            e.Handled = true;
        }
    }

    // ---- Drag/drop reorder ------------------------------------------------

    private void OnTreePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragCandidate = NodeUnder(e.OriginalSource as DependencyObject);
    }

    private void OnTreePreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null)
        {
            return;
        }

        var delta = e.GetPosition(null) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(Tree, _dragCandidate, DragDropEffects.Move);
        _dragCandidate = null;
    }

    private void OnTreeDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;

        if (Vm is not null && e.Data.GetData(typeof(SessionNodeViewModel)) is SessionNodeViewModel source)
        {
            var target = NodeUnder(e.OriginalSource as DependencyObject);
            if (Vm.CanDrop(source, target))
            {
                e.Effects = DragDropEffects.Move;
            }
        }

        e.Handled = true;
    }

    private async void OnTreeDrop(object sender, DragEventArgs e)
    {
        if (Vm is null || e.Data.GetData(typeof(SessionNodeViewModel)) is not SessionNodeViewModel source)
        {
            return;
        }

        var target = NodeUnder(e.OriginalSource as DependencyObject);
        e.Handled = true;
        await Vm.MoveAsync(source, target);
    }

    // ---- Helpers -------------------------------------------------------

    private static SessionNodeViewModel? NodeUnder(DependencyObject? source)
    {
        while (source is not null and not TreeViewItem)
        {
            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return (source as TreeViewItem)?.DataContext as SessionNodeViewModel;
    }
}
