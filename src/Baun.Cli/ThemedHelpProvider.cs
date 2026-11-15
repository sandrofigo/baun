using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Help;
using Spectre.Console.Rendering;

namespace Baun.Cli;

/// <summary>
/// Writes Spectre.Console's help with only the section headings, such as <c>USAGE:</c>, in the primary color.
/// </summary>
/// <remarks>
/// Spectre.Console's help styles can't leave text in the default color: it turns each style into markup, and an unset
/// style becomes empty markup, which fails (setting <see cref="ICommandAppSettings.HelpProviderStyles"/> to
/// <see langword="null"/> fails the same way). So the help is rendered with the default styles to a console without
/// colors, and the heading lines are colored afterwards.
/// </remarks>
/// <param name="settings">The settings of the app.</param>
/// <param name="console">The console the help is written to.</param>
internal sealed class ThemedHelpProvider(ICommandAppSettings settings, IAnsiConsole console) : HelpProvider(settings)
{
    public override IEnumerable<IRenderable> Write(ICommandModel model, ICommandInfo? command)
    {
        var writer = new StringWriter();
        IAnsiConsole plain = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });

        // Spectre.Console turns ANSI back on in some environments, such as GitHub Actions, despite AnsiSupport.No.
        plain.Profile.Capabilities.Ansi = false;
        plain.Profile.Width = console.Profile.Width;
        plain.Profile.Capabilities.Unicode = console.Profile.Capabilities.Unicode;

        foreach (IRenderable renderable in base.Write(model, command))
        {
            plain.Write(renderable);
        }

        var lines = new List<string>();
        string? section = null;

        foreach (string rawLine in writer.ToString().ReplaceLineEndings("\n").TrimEnd('\n').Split('\n'))
        {
            string line = rawLine.TrimEnd();

            if (IsHeading(line))
            {
                section = line;
                lines.Add(Theme.Accent(line));
            }
            else if (section == "USAGE:")
            {
                // Placeholders such as [steps] and [OPTIONS] are faint, the commands are not.
                lines.Add(string.Concat(_placeholder.Split(line).Select(part => _placeholder.IsMatch(part) ? Theme.Faint(part) : Markup.Escape(part))));
            }
            else
            {
                lines.Add(Markup.Escape(line));
            }
        }

        return [new Markup(string.Join('\n', lines) + "\n")];
    }

    /// <summary>
    /// Matches a placeholder in a usage line, such as <c>[steps]</c> or <c>&lt;name&gt;</c>.
    /// </summary>
    private static readonly Regex _placeholder = new(@"(\[[^\]]+\]|<[^>]+>)");

    /// <summary>
    /// Returns whether <paramref name="line"/> is a section heading, an unindented upper-case line ending with a colon.
    /// </summary>
    private static bool IsHeading(string line) =>
        line.Length > 1 &&
        line[^1] == ':' &&
        !char.IsWhiteSpace(line[0]) &&
        line[..^1].All(c => char.IsUpper(c) || c == ' ');
}
