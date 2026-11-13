using Spectre.Console;

namespace BaunCli;

/// <summary>
/// Creates the console the baun tool writes to.
/// </summary>
internal static class CliConsole
{
    /// <summary>
    /// The width to use when the terminal width can't be detected, for example when output is redirected in CI.
    /// </summary>
    private const int FallbackWidth = 120;

    public static IAnsiConsole Create()
    {
        var console = AnsiConsole.Console;

        // With redirected output, .NET can report a width of -1, which would make Spectre.Console
        // truncate every line to an ellipsis.
        if (console.Profile.Width <= 0)
        {
            console.Profile.Width = FallbackWidth;
        }

        return console;
    }
}
