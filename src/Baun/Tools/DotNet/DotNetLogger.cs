using System.Text.RegularExpressions;
using Baun.Logging;

namespace Baun.Tools.DotNet;

/// <summary>
/// Writes the output of <c>dotnet</c> to the log, with errors and warnings of MSBuild and NuGet as
/// <see cref="LogLevel.Error"/> and <see cref="LogLevel.Warning"/>.
/// </summary>
/// <remarks>
/// A line is an error or a warning when it has an MSBuild diagnostic, such as
/// <c>Program.cs(3,1): error CS1002: ; expected</c> or <c>warning NETSDK1138: ...</c>, or starts with <c>error:</c>,
/// <c>warning:</c> or <c>warn :</c>, as NuGet writes them. Other lines are logged like <see cref="ToolLogger.Default"/>
/// does. Override <see cref="GetDiagnosticLevel"/> to detect other diagnostics, or <see cref="GetLevel"/> to pick the
/// level of any line, and set it with <see cref="DotNet.Logger"/> or <see cref="ToolCommand.Logger"/>.
/// </remarks>
public partial class DotNetLogger : ToolLogger
{
    /// <inheritdoc/>
    protected internal override LogLevel GetLevel(ToolOutputLine line) =>
        GetDiagnosticLevel(line.Text) ?? base.GetLevel(line);

    /// <summary>
    /// Returns <see cref="LogLevel.Error"/> or <see cref="LogLevel.Warning"/> if <paramref name="text"/> is an error or a
    /// warning, or <see langword="null"/> if it's neither.
    /// </summary>
    /// <param name="text">The line that <c>dotnet</c> wrote.</param>
    protected virtual LogLevel? GetDiagnosticLevel(string text)
    {
        Match match = Diagnostic().Match(text);

        if (!match.Success)
        {
            return null;
        }

        return match.Groups["error"].Success ? LogLevel.Error : LogLevel.Warning;
    }

    // An MSBuild diagnostic with a code, such as "error CS1002:", or a line that starts with "error:" or "warn :".
    [GeneratedRegex(@"(?:\b(?:(?<error>error)|warning)\s+[A-Za-z]+\d+\s*:)|(?:^\s*(?:(?<error>error)|warn(?:ing)?)\s*:)")]
    private static partial Regex Diagnostic();
}
