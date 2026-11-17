namespace Baun.Logging;

/// <summary>
/// Writes log messages, each on a line that starts with the time and the level, such as <c>14:03:52 INF Restoring</c>.
/// </summary>
/// <remarks>
/// <para>
/// Messages are message templates: name the values in braces and pass them in the same order, such as
/// <c>Log.Information("Built {Project} in {Configuration}", project.Name, Configuration)</c>. The values are
/// highlighted in the output. Use <c>{{</c> and <c>}}</c> for literal braces.
/// </para>
/// <para>
/// Builds write messages of <see cref="LogLevel.Information"/> and above. Pass <c>--verbosity &lt;level&gt;</c> or set
/// <c>BAUN_VERBOSITY</c> to change that, such as <c>--verbosity debug</c>.
/// </para>
/// </remarks>
public static class Log
{
    private static readonly Lazy<ILogBackend> defaultBackend =
        new(() => SerilogBackend.ForConsole(BaunConsole.Create(), LogLevel.Information));

    /// <summary>
    /// The backend of the build that runs in the current asynchronous flow, so builds that run in parallel, such as in
    /// tests, each write to their own console.
    /// </summary>
    private static readonly AsyncLocal<ILogBackend?> current = new();

    private static ILogBackend Backend => current.Value ?? defaultBackend.Value;

    /// <summary>
    /// Returns whether messages of <paramref name="level"/> are written.
    /// </summary>
    public static bool IsEnabled(LogLevel level) => Backend.IsEnabled(level);

    /// <summary>
    /// Writes a message of <see cref="LogLevel.Verbose"/>.
    /// </summary>
    /// <param name="messageTemplate">The message, with the names of its values in braces.</param>
    /// <param name="propertyValues">The values of the names in the message, in order.</param>
    [MessageTemplateFormatMethod("messageTemplate")]
    public static void Verbose(string messageTemplate, params object?[] propertyValues) =>
        Write(LogLevel.Verbose, null, messageTemplate, propertyValues);

    /// <summary>
    /// Writes a message of <see cref="LogLevel.Verbose"/> followed by an exception.
    /// </summary>
    /// <param name="exception">The exception to write after the message.</param>
    /// <param name="messageTemplate">The message, with the names of its values in braces.</param>
    /// <param name="propertyValues">The values of the names in the message, in order.</param>
    [MessageTemplateFormatMethod("messageTemplate")]
    public static void Verbose(Exception? exception, string messageTemplate, params object?[] propertyValues) =>
        Write(LogLevel.Verbose, exception, messageTemplate, propertyValues);

    /// <summary>
    /// Writes a message of <see cref="LogLevel.Debug"/>.
    /// </summary>
    /// <param name="messageTemplate">The message, with the names of its values in braces.</param>
    /// <param name="propertyValues">The values of the names in the message, in order.</param>
    [MessageTemplateFormatMethod("messageTemplate")]
    public static void Debug(string messageTemplate, params object?[] propertyValues) =>
        Write(LogLevel.Debug, null, messageTemplate, propertyValues);

    /// <summary>
    /// Writes a message of <see cref="LogLevel.Debug"/> followed by an exception.
    /// </summary>
    /// <param name="exception">The exception to write after the message.</param>
    /// <param name="messageTemplate">The message, with the names of its values in braces.</param>
    /// <param name="propertyValues">The values of the names in the message, in order.</param>
    [MessageTemplateFormatMethod("messageTemplate")]
    public static void Debug(Exception? exception, string messageTemplate, params object?[] propertyValues) =>
        Write(LogLevel.Debug, exception, messageTemplate, propertyValues);

    /// <summary>
    /// Writes a message of <see cref="LogLevel.Information"/>.
    /// </summary>
    /// <param name="messageTemplate">The message, with the names of its values in braces.</param>
    /// <param name="propertyValues">The values of the names in the message, in order.</param>
    [MessageTemplateFormatMethod("messageTemplate")]
    public static void Information(string messageTemplate, params object?[] propertyValues) =>
        Write(LogLevel.Information, null, messageTemplate, propertyValues);

    /// <summary>
    /// Writes a message of <see cref="LogLevel.Information"/> followed by an exception.
    /// </summary>
    /// <param name="exception">The exception to write after the message.</param>
    /// <param name="messageTemplate">The message, with the names of its values in braces.</param>
    /// <param name="propertyValues">The values of the names in the message, in order.</param>
    [MessageTemplateFormatMethod("messageTemplate")]
    public static void Information(Exception? exception, string messageTemplate, params object?[] propertyValues) =>
        Write(LogLevel.Information, exception, messageTemplate, propertyValues);

    /// <summary>
    /// Writes a message of <see cref="LogLevel.Warning"/>.
    /// </summary>
    /// <param name="messageTemplate">The message, with the names of its values in braces.</param>
    /// <param name="propertyValues">The values of the names in the message, in order.</param>
    [MessageTemplateFormatMethod("messageTemplate")]
    public static void Warning(string messageTemplate, params object?[] propertyValues) =>
        Write(LogLevel.Warning, null, messageTemplate, propertyValues);

    /// <summary>
    /// Writes a message of <see cref="LogLevel.Warning"/> followed by an exception.
    /// </summary>
    /// <param name="exception">The exception to write after the message.</param>
    /// <param name="messageTemplate">The message, with the names of its values in braces.</param>
    /// <param name="propertyValues">The values of the names in the message, in order.</param>
    [MessageTemplateFormatMethod("messageTemplate")]
    public static void Warning(Exception? exception, string messageTemplate, params object?[] propertyValues) =>
        Write(LogLevel.Warning, exception, messageTemplate, propertyValues);

    /// <summary>
    /// Writes a message of <see cref="LogLevel.Error"/>.
    /// </summary>
    /// <param name="messageTemplate">The message, with the names of its values in braces.</param>
    /// <param name="propertyValues">The values of the names in the message, in order.</param>
    [MessageTemplateFormatMethod("messageTemplate")]
    public static void Error(string messageTemplate, params object?[] propertyValues) =>
        Write(LogLevel.Error, null, messageTemplate, propertyValues);

    /// <summary>
    /// Writes a message of <see cref="LogLevel.Error"/> followed by an exception.
    /// </summary>
    /// <param name="exception">The exception to write after the message.</param>
    /// <param name="messageTemplate">The message, with the names of its values in braces.</param>
    /// <param name="propertyValues">The values of the names in the message, in order.</param>
    [MessageTemplateFormatMethod("messageTemplate")]
    public static void Error(Exception? exception, string messageTemplate, params object?[] propertyValues) =>
        Write(LogLevel.Error, exception, messageTemplate, propertyValues);

    /// <summary>
    /// Writes a message of <see cref="LogLevel.Fatal"/>.
    /// </summary>
    /// <param name="messageTemplate">The message, with the names of its values in braces.</param>
    /// <param name="propertyValues">The values of the names in the message, in order.</param>
    [MessageTemplateFormatMethod("messageTemplate")]
    public static void Fatal(string messageTemplate, params object?[] propertyValues) =>
        Write(LogLevel.Fatal, null, messageTemplate, propertyValues);

    /// <summary>
    /// Writes a message of <see cref="LogLevel.Fatal"/> followed by an exception.
    /// </summary>
    /// <param name="exception">The exception to write after the message.</param>
    /// <param name="messageTemplate">The message, with the names of its values in braces.</param>
    /// <param name="propertyValues">The values of the names in the message, in order.</param>
    [MessageTemplateFormatMethod("messageTemplate")]
    public static void Fatal(Exception? exception, string messageTemplate, params object?[] propertyValues) =>
        Write(LogLevel.Fatal, exception, messageTemplate, propertyValues);

    /// <summary>
    /// Writes <paramref name="text"/> as it is, without reading it as a message template.
    /// </summary>
    internal static void WriteText(LogLevel level, string text) =>
        Write(level, null, text.Replace("{", "{{").Replace("}", "}}"), []);

    /// <summary>
    /// Writes the messages of the current asynchronous flow to <paramref name="backend"/> until the returned scope is
    /// disposed.
    /// </summary>
    internal static IDisposable Use(ILogBackend backend)
    {
        ILogBackend? previous = current.Value;
        current.Value = backend;
        return new Scope(() => current.Value = previous);
    }

    private static void Write(LogLevel level, Exception? exception, string messageTemplate, object?[]? propertyValues)
    {
        ArgumentNullException.ThrowIfNull(messageTemplate);

        ILogBackend backend = Backend;

        if (backend.IsEnabled(level))
        {
            // A single null argument arrives as a null array, such as Log.Information("{Value}", null).
            backend.Write(level, exception, messageTemplate, propertyValues ?? [null]);
        }
    }

    private sealed class Scope(Action end) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                end();
            }
        }
    }
}
