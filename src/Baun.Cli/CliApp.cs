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
internal sealed record CliContext(string Directory, string BaunVersion);

internal static class CliApp
{
    /// <summary>
    /// Runs the CLI.
    /// </summary>
    /// <remarks>
    /// If a build is found, all arguments are passed to it unchanged, so <c>baun init</c> runs a step called
    /// <c>Init</c> when the build has one. The CLI's own commands, such as <c>init</c>, are only available when
    /// no build exists yet.
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
        var build = BuildLocator.Find(directory);

        if (build is not null)
        {
            return await runProcess(CreateRunCommand(build, arguments));
        }

        var registrar = new TypeRegistrar();
        registrar.RegisterInstance(typeof(CliContext), new CliContext(directory, GetBaunVersion()));
        registrar.RegisterInstance(typeof(IAnsiConsole), console);

        var app = new CommandApp<NoBuildCommand>(registrar);

        app.Configure(config =>
        {
            config.SetApplicationName("baun");
            config.SetApplicationVersion(GetBaunVersion());
            config.ConfigureConsole(console);
            config.UseStrictParsing();

            config.AddCommand<InitCommand>("init")
                .WithDescription("Creates a new build in the current folder.")
                .WithExample("init")
                .WithExample("init", "--single-file")
                .WithExample("init", "--project");
        });

        return await app.RunAsync(arguments);
    }

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

        foreach (var argument in arguments)
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
        var version = typeof(CliApp).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.1.0";

        // Drop build metadata such as "+3a69508".
        var metadata = version.IndexOf('+');
        return metadata >= 0 ? version[..metadata] : version;
    }
}

internal static class ProcessRunner
{
    public static async Task<int> RunAsync(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{startInfo.FileName}'.");

        await process.WaitForExitAsync();

        return process.ExitCode;
    }
}
