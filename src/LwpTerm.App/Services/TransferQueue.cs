using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using LwpTerm.App.ViewModels.Panels;
using LwpTerm.Core.Transfer;
using Microsoft.Extensions.Logging;

namespace LwpTerm.App.Services;

public sealed record TransferRequest(
    IFileTransferConnection Connection,
    TransferDirection Direction,
    string LocalPath,
    string RemotePath,
    string DisplayName,
    long Size,
    Action? OnCompleted = null);

/// <summary>
/// Runs file transfers with limited concurrency and surfaces each as a
/// <see cref="TransferItemViewModel"/> in the docked Transfers panel.
/// </summary>
public sealed class TransferQueue
{
    private readonly TransfersViewModel _panel;
    private readonly ILogger<TransferQueue> _log;
    private readonly SemaphoreSlim _slots = new(2, 2);

    public TransferQueue(TransfersViewModel panel, ILogger<TransferQueue> log)
    {
        _panel = panel;
        _log = log;
    }

    public void Enqueue(TransferRequest request)
    {
        var item = new TransferItemViewModel(
            request.DisplayName,
            request.Direction == TransferDirection.Upload ? $"→ {request.RemotePath}" : $"← {request.RemotePath}",
            request.Direction,
            request.Size);

        RunOnUi(() => _panel.Items.Add(item));
        _ = RunAsync(request, item);
    }

    private async Task RunAsync(TransferRequest request, TransferItemViewModel item)
    {
        await _slots.WaitAsync().ConfigureAwait(false);
        try
        {
            if (item.CancellationToken.IsCancellationRequested)
            {
                RunOnUi(() => item.Status = TransferStatus.Cancelled);
                return;
            }

            RunOnUi(item.MarkStarted);

            var progress = new Progress<TransferProgress>(p => RunOnUi(() => item.ReportBytes(p.BytesTransferred)));

            if (request.Direction == TransferDirection.Upload)
            {
                await request.Connection
                    .UploadAsync(request.LocalPath, request.RemotePath, progress, item.CancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await request.Connection
                    .DownloadAsync(request.RemotePath, request.LocalPath, progress, item.CancellationToken)
                    .ConfigureAwait(false);
            }

            RunOnUi(() =>
            {
                item.ReportBytes(request.Size > 0 ? request.Size : item.TransferredBytes);
                item.Status = TransferStatus.Completed;
            });
            request.OnCompleted?.Invoke();
        }
        catch (OperationCanceledException)
        {
            RunOnUi(() => item.Status = TransferStatus.Cancelled);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Transfer failed: {Name}", request.DisplayName);
            RunOnUi(() =>
            {
                item.Error = ex.Message;
                item.Status = TransferStatus.Failed;
            });
        }
        finally
        {
            _slots.Release();
        }
    }

    private static void RunOnUi(Action action)
    {
        var app = Application.Current;
        if (app is null || app.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            app.Dispatcher.BeginInvoke(action);
        }
    }
}
