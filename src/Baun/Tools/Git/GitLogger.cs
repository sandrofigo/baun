using Baun.Logging;

namespace Baun.Tools.Git;

/// <summary>
/// Writes the output of <c>git</c> to the log, with its errors and warnings as <see cref="LogLevel.Error"/> and
/// <see cref="LogLevel.Warning"/>.
/// </summary>
/// <remarks>
/// git writes progress and hints to its standard error too, so a line there is only an error when it starts with
/// <c>fatal:</c> or <c>error:</c>, and only a warning when it starts with <c>warning:</c>. Other lines are logged as
/// <see cref="LogLevel.Debug"/>. Override <see cref="GetStandardErrorLevel"/> to change the level of lines on the
/// standard error, or <see cref="GetLevel"/> for any line, and set it with <see cref="Git.Logger"/> or
/// <see cref="ToolCommand.Logger"/>.
/// </remarks>
public class GitLogger : ToolLogger
{
    /// <inheritdoc/>
    protected internal override LogLevel GetLevel(ToolOutputLine line) =>
        line.Stream == ToolStream.StandardError ? GetStandardErrorLevel(line.Text) : LogLevel.Debug;

    /// <summary>
    /// Returns the level of <paramref name="text"/>, a line that git wrote to its standard error.
    /// </summary>
    /// <param name="text">The line that git wrote.</param>
    protected virtual LogLevel GetStandardErrorLevel(string text)
    {
        ReadOnlySpan<char> trimmed = text.AsSpan().TrimStart();

        if (trimmed.StartsWith("fatal:", StringComparison.Ordinal) || trimmed.StartsWith("error:", StringComparison.Ordinal))
        {
            return LogLevel.Error;
        }

        if (trimmed.StartsWith("warning:", StringComparison.Ordinal))
        {
            return LogLevel.Warning;
        }

        return LogLevel.Debug;
    }
}
