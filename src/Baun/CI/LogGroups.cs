using System.Text;
using Spectre.Console;

namespace Baun.CI;

/// <summary>
/// Writes the log groups of CI servers. Groups don't nest: a group opened inside another one is written as a plain line.
/// </summary>
internal static class LogGroups
{
    /// <summary>
    /// The number of groups open in the current asynchronous flow, so builds that run in parallel, such as in tests,
    /// don't share it.
    /// </summary>
    private static readonly AsyncLocal<int> depth = new();

    private static int gitLabSections;

    /// <summary>
    /// Opens a log group named <paramref name="name"/>.
    /// </summary>
    /// <param name="console">The console to write to.</param>
    /// <param name="provider">The CI server to write the group for.</param>
    /// <param name="name">The name of the group.</param>
    /// <param name="fallbackMarkup">
    /// The line, as markup, to write instead of a group outside CI or inside another group.
    /// </param>
    /// <param name="padFallback">Whether to write an empty line before and after <paramref name="fallbackMarkup"/>.</param>
    /// <returns>A scope that ends the group when it's disposed.</returns>
    public static IDisposable Begin(
        IAnsiConsole console,
        CIProvider provider,
        string name,
        string fallbackMarkup,
        bool padFallback = false)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(name);

        int level = depth.Value + 1;
        depth.Value = level;

        if (level == 1 && provider != CIProvider.None)
        {
            string singleLine = name.ReplaceLineEndings(" ");
            string? gitLabId = provider == CIProvider.GitLab ? GitLabSectionId(singleLine) : null;

            WriteRaw(console, StartMarker(provider, singleLine, gitLabId));
            return new Scope(level, () => WriteRaw(console, EndMarker(provider, singleLine, gitLabId)));
        }

        if (padFallback)
        {
            console.WriteLine();
        }

        console.MarkupLine(fallbackMarkup);

        if (padFallback)
        {
            console.WriteLine();
        }

        return new Scope(level, null);
    }

    private static string StartMarker(CIProvider provider, string name, string? gitLabId) => provider switch
    {
        CIProvider.GitHubActions => $"::group::{name}",
        CIProvider.AzurePipelines => $"##[group]{name}",
        CIProvider.GitLab => $"\e[0Ksection_start:{UnixTime()}:{gitLabId}[collapsed=true]\r\e[0K{name}",
        CIProvider.TeamCity => $"##teamcity[blockOpened name='{EscapeTeamCity(name)}']",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    private static string EndMarker(CIProvider provider, string name, string? gitLabId) => provider switch
    {
        CIProvider.GitHubActions => "::endgroup::",
        CIProvider.AzurePipelines => "##[endgroup]",
        CIProvider.GitLab => $"\e[0Ksection_end:{UnixTime()}:{gitLabId}\r\e[0K",
        CIProvider.TeamCity => $"##teamcity[blockClosed name='{EscapeTeamCity(name)}']",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    /// <summary>
    /// Writes a marker unchanged, so the console can't wrap or style it.
    /// </summary>
    private static void WriteRaw(IAnsiConsole console, string marker)
    {
        TextWriter writer = console.Profile.Out.Writer;
        writer.WriteLine(marker);
        writer.Flush();
    }

    private static long UnixTime() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>
    /// Returns a unique section ID for <paramref name="name"/>. GitLab only accepts letters, digits, <c>_</c>, <c>.</c>
    /// and <c>-</c> in it.
    /// </summary>
    internal static string GitLabSectionId(string name)
    {
        var id = new StringBuilder(name.Length);

        foreach (char c in name)
        {
            id.Append(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-' ? char.ToLowerInvariant(c) : '_');
        }

        return $"{id}_{Interlocked.Increment(ref gitLabSections)}";
    }

    /// <summary>
    /// Escapes <paramref name="value"/> for a TeamCity service message.
    /// </summary>
    internal static string EscapeTeamCity(string value)
    {
        var escaped = new StringBuilder(value.Length);

        foreach (char c in value)
        {
            escaped.Append(c switch
            {
                '|' => "||",
                '\'' => "|'",
                '[' => "|[",
                ']' => "|]",
                '\n' => "|n",
                '\r' => "|r",
                _ => c.ToString(),
            });
        }

        return escaped.ToString();
    }

    private sealed class Scope(int level, Action? end) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            end?.Invoke();

            // Also ends groups opened inside this one that weren't disposed.
            depth.Value = level - 1;
        }
    }
}
