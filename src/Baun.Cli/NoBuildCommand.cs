using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaunCli;

/// <summary>
/// Runs when no build exists and no other command was given, for example <c>baun Test</c> in a folder without a build.
/// </summary>
internal sealed class NoBuildCommand(IAnsiConsole console) : Command<NoBuildCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[steps]")]
        [Description("The steps to run. Requires a build in the current folder or its 'build' subfolder.")]
        public string[] Steps { get; init; } = [];
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        console.MarkupLine(
            "[red bold]error:[/] No baun build found. Expected a single-file [blue]Build.cs[/], or a [blue]Build.cs[/] next to a project file, " +
            "in the current folder or its [blue]build[/] subfolder.");
        console.MarkupLine("Run [green]baun init[/] to create one.");
        return 1;
    }
}
