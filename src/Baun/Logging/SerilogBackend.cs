using Serilog;
using Serilog.Events;

namespace Baun.Logging;

/// <summary>
/// Writes the messages of <see cref="Log"/> with Serilog.
/// </summary>
internal sealed class SerilogBackend(ILogger logger) : ILogBackend, IDisposable
{
    /// <summary>
    /// Creates a backend that writes messages of <paramref name="minimumLevel"/> and above to <paramref name="console"/>.
    /// Dispose it to flush the logger.
    /// </summary>
    public static SerilogBackend ForConsole(Spectre.Console.IAnsiConsole console, LogLevel minimumLevel) =>
        new(new LoggerConfiguration()
            .MinimumLevel.Is(ToSerilog(minimumLevel))
            .Destructure.AsScalar<CommandLine>()
            .WriteTo.Sink(new AnsiConsoleSink(console))
            .CreateLogger());

    public bool IsEnabled(LogLevel level) => logger.IsEnabled(ToSerilog(level));

    public void Write(LogLevel level, Exception? exception, string messageTemplate, object?[] propertyValues) =>
        logger.Write(ToSerilog(level), exception, messageTemplate, propertyValues);

    public void Dispose() => (logger as IDisposable)?.Dispose();

    internal static LogEventLevel ToSerilog(LogLevel level) => level switch
    {
        LogLevel.Verbose => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        LogLevel.Fatal => LogEventLevel.Fatal,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };
}
