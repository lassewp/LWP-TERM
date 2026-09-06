using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LwpTerm.App.ViewModels.FileBrowser;
using LwpTerm.App.ViewModels.Tabs;

namespace LwpTerm.App.Views.Tabs;

public partial class FileBrowserView : UserControl
{
    private const string DragFormat = "LwpTerm.FileEntries";

    private Point _dragStart;
    private DataGrid? _dragSourceGrid;

    public FileBrowserView()
    {
        InitializeComponent();
        Loaded += (_, _) => (DataContext as FileBrowserTabViewModel)?.Initialize();
    }

    private FileBrowserTabViewModel? Vm => DataContext as FileBrowserTabViewModel;

    // ---- open directories -------------------------------------------------

    private async void OnLocalDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not null && EntryUnder(e.OriginalSource) is { IsDirectory: true } entry)
        {
            await Vm.Local.OpenAsync(entry);
        }
    }

    private async void OnRemoteDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not null && EntryUnder(e.OriginalSource) is { IsDirectory: true } entry)
        {
            await Vm.Remote.OpenAsync(entry);
        }
    }

    // ---- drag start ----------------------------------------------------

    private void OnGridPreviewLeftDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragSourceGrid = sender as DataGrid;
    }

    private void OnGridPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragSourceGrid is null || sender != _dragSourceGrid)
        {
            return;
        }

        var delta = e.GetPosition(null) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var selected = _dragSourceGrid.SelectedItems.OfType<FileEntryViewModel>().ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var data = new DataObject();
        data.SetData(DragFormat, selected);
        data.SetData("SourceIsLocal", ReferenceEquals(_dragSourceGrid, LocalGrid));
        DragDrop.DoDragDrop(_dragSourceGrid, data, DragDropEffects.Copy);
    }

    private void OnGridDragOver(object sender, DragEventArgs e)
    {
        e.Effects = IsValidCrossPaneDrop(e, toRemote: ReferenceEquals(sender, RemoteGrid))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnLocalDrop(object sender, DragEventArgs e) => HandleDrop(e, toRemote: false);

    private void OnRemoteDrop(object sender, DragEventArgs e) => HandleDrop(e, toRemote: true);

    private void HandleDrop(DragEventArgs e, bool toRemote)
    {
        e.Handled = true;
        if (Vm is null || !IsValidCrossPaneDrop(e, toRemote))
        {
            return;
        }

        if (e.Data.GetData(DragFormat) is IEnumerable<FileEntryViewModel> entries)
        {
            Vm.TransferDropped(toRemote, entries);
        }
    }

    private static bool IsValidCrossPaneDrop(DragEventArgs e, bool toRemote)
    {
        if (!e.Data.GetDataPresent(DragFormat) || !e.Data.GetDataPresent("SourceIsLocal"))
        {
            return false;
        }

        var fromLocal = (bool)e.Data.GetData("SourceIsLocal")!;

        // Valid only when source and target are opposite sides:
        //   local -> remote (upload)  |  remote -> local (download)
        return fromLocal == toRemote;
    }

    // ---- helpers -----------------------------------------------------

    private static FileEntryViewModel? EntryUnder(object? source)
    {
        var d = source as DependencyObject;
        while (d is not null and not DataGridRow)
        {
            d = d is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }

        return (d as DataGridRow)?.Item as FileEntryViewModel;
    }
}
