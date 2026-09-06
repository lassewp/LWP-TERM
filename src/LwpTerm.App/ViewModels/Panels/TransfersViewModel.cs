using System;
using System.Collections.ObjectModel;
using System.Threading;
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
    private readonly CancellationTokenSource _cts = new();
    private DateTime _startedUtc;
    private long _lastBytes;
    private DateTime _lastSampleUtc;

    public TransferItemViewModel(string name, string detail, TransferDirection direction, long totalBytes)
    {
        Name = name;
        Detail = detail;
        Direction = direction;
        TotalBytes = totalBytes;
    }

    public string Name { get; }

    public string Detail { get; }

    public TransferDirection Direction { get; }

    public long TotalBytes { get; }

    public CancellationToken CancellationToken => _cts.Token;

    [ObservableProperty]
    private long _transferredBytes;

    [ObservableProperty]
    private double _bytesPerSecond;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFinished))]
    private TransferStatus _status = TransferStatus.Queued;

    [ObservableProperty]
    private string? _error;

    public bool IsFinished => Status is TransferStatus.Completed or TransferStatus.Failed or TransferStatus.Cancelled;

    public double ProgressPercent =>
        TotalBytes <= 0 ? (Status == TransferStatus.Completed ? 100 : 0)
                        : Math.Clamp(TransferredBytes * 100.0 / TotalBytes, 0, 100);

    public string SpeedText => BytesPerSecond <= 0 ? "" : $"{FormatBytes((long)BytesPerSecond)}/s";

    public string SizeText => $"{FormatBytes(TransferredBytes)} / {(TotalBytes > 0 ? FormatBytes(TotalBytes) : "?")}";

    public void MarkStarted()
    {
        Status = TransferStatus.Running;
        _startedUtc = DateTime.UtcNow;
        _lastSampleUtc = _startedUtc;
    }

    public void ReportBytes(long transferred)
    {
        TransferredBytes = transferred;

        var now = DateTime.UtcNow;
        var span = (now - _lastSampleUtc).TotalSeconds;
        if (span >= 0.5)
        {
            BytesPerSecond = (transferred - _lastBytes) / span;
            _lastBytes = transferred;
            _lastSampleUtc = now;
        }
    }

    partial void OnTransferredBytesChanged(long value)
    {
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(SizeText));
    }

    partial void OnBytesPerSecondChanged(double value) => OnPropertyChanged(nameof(SpeedText));

    [RelayCommand]
    private void Cancel()
    {
        if (!IsFinished)
        {
            _cts.Cancel();
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }
}

/// <summary>Backs the docked "Transfers" panel.</summary>
public sealed partial class TransfersViewModel : ObservableObject
{
    public ObservableCollection<TransferItemViewModel> Items { get; } = new();

    [RelayCommand]
    private void ClearFinished()
    {
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (Items[i].IsFinished)
            {
                Items.RemoveAt(i);
            }
        }
    }
}
