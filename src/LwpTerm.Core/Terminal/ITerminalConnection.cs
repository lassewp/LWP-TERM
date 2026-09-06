using System;
using System.Threading;
using System.Threading.Tasks;

namespace LwpTerm.Core.Terminal;

/// <summary>
/// A byte-stream transport that drives a single terminal surface: a local
/// ConPTY shell, an SSH shell channel, a Telnet session or a serial port.
/// Implementations raise <see cref="DataReceived"/> from a background thread;
/// the consumer marshals to the UI.
/// </summary>
public interface ITerminalConnection : IAsyncDisposable
{
    /// <summary>Bytes produced by the remote end. The buffer is only valid for the duration of the call.</summary>
    event EventHandler<ReadOnlyMemory<byte>> DataReceived;

    /// <summary>Raised once when the transport closes. Argument is the fault, or null for a clean exit.</summary>
    event EventHandler<Exception?> Closed;

    bool IsConnected { get; }

    Task ConnectAsync(int columns, int rows, CancellationToken cancellationToken = default);

    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default);
}
