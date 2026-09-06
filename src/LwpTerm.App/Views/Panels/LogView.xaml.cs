using System.Collections.Specialized;
using System.Windows.Controls;
using LwpTerm.App.ViewModels.Panels;

namespace LwpTerm.App.Views.Panels;

public partial class LogView : UserControl
{
    public LogView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook();
    }

    private void Hook()
    {
        if (DataContext is LogViewModel vm)
        {
            vm.Lines.CollectionChanged -= OnLinesChanged;
            vm.Lines.CollectionChanged += OnLinesChanged;
        }
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && List.Items.Count > 0)
        {
            List.ScrollIntoView(List.Items[^1]);
        }
    }
}
