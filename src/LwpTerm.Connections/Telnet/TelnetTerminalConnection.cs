using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LwpTerm.Core.Sessions;
using LwpTerm.Core.Terminal;
using Microsoft.Extensions.Logging;

namespace LwpTerm.Connections.Telnet;

/// <summary>
/// <see cref="ITerminalConnection"/> for a raw Telnet session: a TCP socket plus
/// <see cref="TelnetOptionNegotiator"/> for IAC handling. Everything that is not
/// an IAC command flows straight to and from the terminal.
/// </summary>
public sealed class TelnetTerminalConnection : ITerminalConnection
{
    private readonly TelnetConnectionSettings _settings;
    private readonly ILogger _log;
    private readonly TelnetOptionNegotiator _negotiator = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private Thread? _reader;
    private volatile bool _closed;

    public TelnetTerminalConnection(TelnetConnectionSettings settings, ILogger log)
    {
        _settings = settings;
        _log = log;
    }

    public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;
    public event EventHandler<Exception?>? Closed;

    public bool IsConnected => _tcp?.Connected == true && !_closed;

    public async Task ConnectAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        if (_tcp is not null)
        {
            throw new InvalidOperationException("Already connected.");
        }

        var port = _settings.Port <= 0 ? 23 : _settings.Port;
        _tcp = new TcpClient { NoDelay = true };
        await _tcp.ConnectAsync(_settings.Host, port, cancellationToken).ConfigureAwait(false);
        _stream = _tcp.GetStream();

        _negotiator.SetWindowSize(columns, rows);

        await _stream.WriteAsync(_negotiator.InitialHandshake(), cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        _reader = new Thread(ReadLoop) { IsBackground = true, Name = $"telnet-reader-{_settings.Host}" };
        _reader.Start();
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var stream = _stream;
        if (stream is null || _closed)
        {
            return;
        }

        // Escape any literal 0xFF in user input so it is not read as IAC.
        var payload = EscapeIac(data.Span);

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            MarkClosed(ex);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        var stream = _stream;
        if (stream is null || _closed)
        {
            return;
        }

        var naws = _negotiator.SetWindowSize(columns, rows);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(naws, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            MarkClosed(ex);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void ReadLoop()
    {
        var buffer = new byte[8192];
        var stream = _stream!;

        try
        {
            while (!_closed)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                _negotiator.Process(buffer.AsSpan(0, read), out var appData, out var response);

                if (response.Length > 0)
                {
                    SendResponse(response);
                }

                if (appData.Length > 0)
                {
                    DataReceived?.Invoke(this, appData);
                }
            }

            MarkClosed(null);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            MarkClosed(null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Telnet reader loop faulted");
            MarkClosed(ex);
        }
    }

    private void SendResponse(byte[] response)
    {
        _writeGate.Wait();
        try
        {
            _stream!.Write(response, 0, response.Length);
            _stream.Flush();
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            MarkClosed(ex);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static byte[] EscapeIac(ReadOnlySpan<byte> input)
    {
        var extra = 0;
        foreach (var b in input)
        {
            if (b == 255)
            {
                extra++;
            }
        }

        if (extra == 0)
        {
            return input.ToArray();
        }

        var output = new byte[input.Length + extra];
        var i = 0;
        foreach (var b in input)
        {
            output[i++] = b;
            if (b == 255)
            {
                output[i++] = 255;
            }
        }

        return output;
    }

    private void MarkClosed(Exception? error)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        Closed?.Invoke(this, error);
    }

    public async ValueTask DisposeAsync()
    {
        _closed = true;

        try { _stream?.Dispose(); } catch { /* ignore */ }
        try { _tcp?.Dispose(); } catch { /* ignore */ }

        if (_reader is { IsAlive: true })
        {
            await Task.Run(() => _reader.Join(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
        }

        _writeGate.Dispose();
    }
}
