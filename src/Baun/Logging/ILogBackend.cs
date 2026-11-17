namespace Baun;

/// <summary>
/// Writes the messages of <see cref="Log"/>, so the logging library behind it can be replaced.
/// </summary>
internal interface ILogBackend
{
    bool IsEnabled(LogLevel level);

    void Write(LogLevel level, Exception? exception, string messageTemplate, object?[] propertyValues);
}
