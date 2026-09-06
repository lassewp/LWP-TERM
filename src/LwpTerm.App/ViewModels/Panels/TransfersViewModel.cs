using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LwpTerm.App.ViewModels.Panels;

public enum TransferDirection
{
    Upload,
    Download
}

public enum TransferStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed partial class TransferItemViewModel : ObservableObject
{
    public TransferItemViewModel(string name, TransferDirection direction, long totalBytes)
    {
        Name = name;
        Direction = direction;
        TotalBytes = totalBytes;
    }

    public string Name { get; }

    public TransferDirection Direction { get; }

    public long TotalBytes { get; }

    [ObservableProperty]
    private long _transferredBytes;

    [ObservableProperty]
    private double _bytesPerSecond;

    [ObservableProperty]
    private TransferStatus _status = TransferStatus.Queued;

    public double ProgressPercent =>
        TotalBytes <= 0 ? 0 : Math.Clamp(TransferredBytes * 100.0 / TotalBytes, 0, 100);

    partial void OnTransferredBytesChanged(long value) => OnPropertyChanged(nameof(ProgressPercent));
}

/// <summary>
/// Backs the docked "Transfers" panel. M0 exposes an empty queue; M4 fills it
/// from the SFTP/FTP browser.
/// </summary>
public sealed partial class TransfersViewModel : ObservableObject
{
    public ObservableCollection<TransferItemViewModel> Items { get; } = new();

    [RelayCommand]
    private void ClearCompleted()
    {
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (Items[i].Status is TransferStatus.Completed or TransferStatus.Cancelled or TransferStatus.Failed)
            {
                Items.RemoveAt(i);
            }
        }
    }
}
