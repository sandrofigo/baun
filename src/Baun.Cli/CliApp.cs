using System.Diagnostics;
using System.Reflection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Baun.Cli;

/// <summary>
/// What the CLI's commands need to know about where and how they run.
/// </summary>
/// <param name="Directory">The folder the CLI was started in.</param>
/// <param name="BaunVersion">The version of the Baun package that new builds reference.</param>
/// <param name="RunProcess">Starts a process and returns its exit code.</param>
/// <param name="Build">The existing build, or <see langword="null"/> when there is none yet.</param>
internal sealed record CliContext(string Directory, string BaunVersion, Func<ProcessStartInfo, Task<int>> RunProcess, BuildLocation? Build);

internal static class CliApp
{
    /// <summary>
    /// Runs the CLI.
    /// </summary>
    /// <remarks>
    /// The CLI's own commands, such as <c>init</c>, always take precedence. Otherwise, if a build is found, all
    /// arguments are passed to it unchanged. A step with the name of a command, such as <c>Init</c>, runs with
    /// <c>baun --step Init</c>.
    /// </remarks>
    /// <param name="arguments">The command-line arguments.</param>
    /// <param name="directory">The folder the CLI was started in.</param>
    /// <param name="console">The console to use.</param>
    /// <param name="runProcess">Starts a process and returns its exit code.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        string directory,
        IAnsiConsole console,
        Func<ProcessStartInfo, Task<int>> runProcess)
    {
        BuildLocation? build = BuildLocator.Find(directory);

        if (build is not null && !IsCommand(arguments))
        {
            return await runProcess(CreateRunCommand(build, arguments));
        }

        var registrar = new TypeRegistrar();
        registrar.RegisterInstance(typeof(CliContext), new CliContext(directory, GetBaunVersion(), runProcess, build));
        registrar.RegisterInstance(typeof(IAnsiConsole), console);

        var app = new CommandApp<NoBuildCommand>(registrar);

        app.Configure(config =>
        {
            config.SetApplicationName("baun");
            config.SetApplicationVersion(GetBaunVersion());
            config.ConfigureConsole(console);
            config.UseStrictParsing();
            config.CaseSensitivity(CaseSensitivity.None); // baun Init works like baun init, as step names do
            config.SetHelpProvider(new ThemedHelpProvider(config.Settings, console));

            config.AddCommand<InitCommand>(InitCommandName)
                .WithDescription("Creates a new build in the current folder, or updates the bootstrap scripts of an existing one.")
                .WithExample("init")
                .WithExample("init", "--single-file")
                .WithExample("init", "--project");
        });

        return await app.RunAsync(arguments);
    }

    private const string InitCommandName = "init";

    /// <summary>
    /// Returns whether <paramref name="arguments"/> start with one of the CLI's own commands.
    /// </summary>
    /// <remarks>
    /// Keep this in sync with the command names the source generator warns about (BAUN007).
    /// </remarks>
    private static bool IsCommand(IReadOnlyList<string> arguments) =>
        arguments.Count > 0 && string.Equals(arguments[0], InitCommandName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Creates the <c>dotnet run</c> command that runs <paramref name="build"/> with <paramref name="arguments"/>.
    /// </summary>
    internal static ProcessStartInfo CreateRunCommand(BuildLocation build, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet");

        startInfo.ArgumentList.Add("run");

        if (build.ProjectFile is not null)
        {
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(build.ProjectFile);
        }
        else
        {
            startInfo.ArgumentList.Add("--file");
            startInfo.ArgumentList.Add(build.BuildFile);
        }

        startInfo.ArgumentList.Add("--");

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    /// <summary>
    /// Gets the version of this tool, which is also the version of baun new builds reference.
    /// </summary>
    private static string GetBaunVersion()
    {
        string version = typeof(CliApp).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        // Drop build metadata such as "+3a69508".
        int metadata = version.IndexOf('+');
        return metadata >= 0 ? version[..metadata] : version;
    }
}

internal static class ProcessRunner
{
    public static async Task<int> RunAsync(ProcessStartInfo startInfo)
    {
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{startInfo.FileName}'.");

        await process.WaitForExitAsync();

        return process.ExitCode;
    }
}
