using System;
using Serilog.Core;
using Serilog.Events;

namespace LwpTerm.App.Services;

/// <summary>
/// A tiny Serilog sink that re-broadcasts formatted log lines to the UI so the
/// docked "Log" panel can show recent activity. The panel subscribes to
/// <see cref="Emitted"/>; nothing is retained here.
/// </summary>
public sealed class InMemoryLogSink : ILogEventSink
{
    public static readonly InMemoryLogSink Instance = new();

    private InMemoryLogSink()
    {
    }

    public event EventHandler<LogLine>? Emitted;

    public void Emit(LogEvent logEvent)
    {
        var line = new LogLine(
            logEvent.Timestamp.LocalDateTime,
            logEvent.Level,
            logEvent.RenderMessage(),
            logEvent.Exception?.ToString());
        Emitted?.Invoke(this, line);
    }
}

public readonly record struct LogLine(
    DateTime Timestamp,
    LogEventLevel Level,
    string Message,
    string? Exception)
{
    public string Display =>
        Exception is null
            ? $"{Timestamp:HH:mm:ss} [{Level.ToString()[..3].ToUpperInvariant()}] {Message}"
            : $"{Timestamp:HH:mm:ss} [{Level.ToString()[..3].ToUpperInvariant()}] {Message}\n{Exception}";
}
