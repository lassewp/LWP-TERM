using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using LwpTerm.Core.Sessions;
using LwpTerm.Core.Terminal;
using Microsoft.Extensions.Logging;

namespace LwpTerm.Connections.Serial;

/// <summary>
/// <see cref="ITerminalConnection"/> over a serial port. No window concept, so
/// <see cref="ResizeAsync"/> is a no-op; optional local echo mirrors typed bytes
/// straight back to the terminal.
/// </summary>
public sealed class SerialTerminalConnection : ITerminalConnection
{
    private readonly SerialConnectionSettings _settings;
    private readonly ILogger _log;

    private SerialPort? _port;
    private volatile bool _closed;

    public SerialTerminalConnection(SerialConnectionSettings settings, ILogger log)
    {
        _settings = settings;
        _log = log;
    }

    public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;
    public event EventHandler<Exception?>? Closed;

    public bool IsConnected => _port?.IsOpen == true && !_closed;

    public Task ConnectAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        if (_port is not null)
        {
            throw new InvalidOperationException("Serial port already open.");
        }

        var port = new SerialPort(_settings.PortName)
        {
            BaudRate = _settings.BaudRate,
            DataBits = _settings.DataBits,
            Parity = MapParity(_settings.Parity),
            StopBits = MapStopBits(_settings.StopBits),
            Handshake = MapHandshake(_settings.Handshake),
            ReadTimeout = SerialPort.InfiniteTimeout,
            WriteTimeout = 2000,
            Encoding = System.Text.Encoding.Latin1
        };

        port.DataReceived += OnDataReceived;
        port.ErrorReceived += (_, e) => _log.LogWarning("Serial error: {Type}", e.EventType);

        try
        {
            port.Open();
            // DTR/RTS on so most devices start talking.
            port.DtrEnable = true;
            port.RtsEnable = _settings.Handshake is not SerialHandshake.RequestToSend
                and not SerialHandshake.RequestToSendXOnXOff;
        }
        catch (Exception ex)
        {
            port.Dispose();
            throw new IOException($"Could not open {_settings.PortName}: {ex.Message}", ex);
        }

        _port = port;
        _log.LogInformation("Opened serial port {Port} @ {Baud}", _settings.PortName, _settings.BaudRate);
        return Task.CompletedTask;
    }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var port = _port;
        if (port is null || !port.IsOpen || _closed)
        {
            return Task.CompletedTask;
        }

        try
        {
            var array = data.ToArray();
            port.Write(array, 0, array.Length);

            if (_settings.LocalEcho)
            {
                DataReceived?.Invoke(this, array);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException)
        {
            MarkClosed(ex);
        }

        return Task.CompletedTask;
    }

    public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default) => Task.CompletedTask;

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        var port = _port;
        if (port is null || !port.IsOpen || _closed)
        {
            return;
        }

        try
        {
            var count = port.BytesToRead;
            if (count <= 0)
            {
                return;
            }

            var buffer = new byte[count];
            var read = port.Read(buffer, 0, count);
            if (read > 0)
            {
                DataReceived?.Invoke(this, new ReadOnlyMemory<byte>(buffer, 0, read));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException)
        {
            MarkClosed(ex);
        }
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

    private static Parity MapParity(SerialParity p) => p switch
    {
        SerialParity.Odd => Parity.Odd,
        SerialParity.Even => Parity.Even,
        SerialParity.Mark => Parity.Mark,
        SerialParity.Space => Parity.Space,
        _ => Parity.None
    };

    private static StopBits MapStopBits(SerialStopBits s) => s switch
    {
        SerialStopBits.OnePointFive => StopBits.OnePointFive,
        SerialStopBits.Two => StopBits.Two,
        _ => StopBits.One
    };

    private static Handshake MapHandshake(SerialHandshake h) => h switch
    {
        SerialHandshake.XOnXOff => Handshake.XOnXOff,
        SerialHandshake.RequestToSend => Handshake.RequestToSend,
        SerialHandshake.RequestToSendXOnXOff => Handshake.RequestToSendXOnXOff,
        _ => Handshake.None
    };

    public ValueTask DisposeAsync()
    {
        _closed = true;

        var port = _port;
        _port = null;
        if (port is not null)
        {
            port.DataReceived -= OnDataReceived;
            try
            {
                if (port.IsOpen)
                {
                    port.Close();
                }
            }
            catch { /* ignore */ }

            port.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
