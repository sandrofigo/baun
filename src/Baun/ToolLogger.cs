using Baun.Logging;

namespace Baun;

/// <summary>
/// The stream of a tool that a line of its output comes from.
/// </summary>
public enum ToolStream
{
    /// <summary>
    /// The standard output.
    /// </summary>
    StandardOutput,

    /// <summary>
    /// The standard error.
    /// </summary>
    StandardError,
}

/// <summary>
/// A line that a tool wrote.
/// </summary>
/// <param name="Stream">The stream the tool wrote the line to.</param>
/// <param name="Text">The line, with secrets as <c>***</c>.</param>
public readonly record struct ToolOutputLine(ToolStream Stream, string Text);

/// <summary>
/// Writes the output of a tool to the log, line by line, and picks the level of each line.
/// </summary>
/// <remarks>
/// <para>
/// The default logger logs each line of the standard output as <see cref="LogLevel.Debug"/>, and each line of the
/// standard error as <see cref="LogLevel.Warning"/>. Derive from it, or from the logger of a tool such as
/// <c>DotNetLogger</c>, and override <see cref="GetLevel"/> to pick other levels, such as <see cref="LogLevel.Error"/>
/// for lines with errors, or <see cref="Write"/> to skip or change lines. Call the base method for the lines you don't
/// handle.
/// </para>
/// <para>
/// A tool's standard output and standard error are read at the same time, so <see cref="Write"/> can be called from two
/// threads at once, and a logger can be used by several commands. Keep loggers without state, or make them thread-safe.
/// </para>
/// </remarks>
public class ToolLogger
{
    /// <summary>
    /// Gets the logger that logs the standard output as <see cref="LogLevel.Debug"/> and the standard error as
    /// <see cref="LogLevel.Warning"/>.
    /// </summary>
    public static ToolLogger Default { get; } = new();

    /// <summary>
    /// Returns the level to log <paramref name="line"/> with.
    /// </summary>
    /// <param name="line">The line the tool wrote.</param>
    protected internal virtual LogLevel GetLevel(ToolOutputLine line) =>
        line.Stream == ToolStream.StandardError ? LogLevel.Warning : LogLevel.Debug;

    /// <summary>
    /// Writes <paramref name="line"/> to the log, with the level of <see cref="GetLevel"/>.
    /// </summary>
    /// <param name="line">The line the tool wrote.</param>
    protected internal virtual void Write(ToolOutputLine line) => WriteLine(GetLevel(line), line.Text);

    /// <summary>
    /// Writes <paramref name="text"/> to the log as it is, so braces in it don't count as message template properties.
    /// </summary>
    /// <param name="level">The level of the message.</param>
    /// <param name="text">The text to write.</param>
    protected static void WriteLine(LogLevel level, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Log.WriteText(level, text);
    }
}
