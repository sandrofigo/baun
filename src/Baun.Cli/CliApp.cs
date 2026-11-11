using System.Diagnostics;
using System.Reflection;

namespace BaunCli;

/// <summary>
/// The console the CLI reads from and writes to.
/// </summary>
/// <param name="IsInteractive">Whether a person can answer questions on <paramref name="In"/>.</param>
internal sealed record CliConsole(TextReader In, TextWriter Out, TextWriter Error, bool IsInteractive);

internal static class CliApp
{
    /// <summary>
    /// Runs the CLI.
    /// </summary>
    /// <remarks>
    /// If a build is found, all arguments are passed to it, so <c>baun setup</c> runs a step called <c>Setup</c>
    /// when the build has one. Only when no build exists does <c>baun setup</c> create a new build.
    /// </remarks>
    /// <param name="arguments">The command-line arguments.</param>
    /// <param name="directory">The folder the CLI was started in.</param>
    /// <param name="console">The console to use.</param>
    /// <param name="runProcess">Starts a process and returns its exit code.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        string directory,
        CliConsole console,
        Func<ProcessStartInfo, Task<int>> runProcess)
    {
        var build = BuildLocator.Find(directory);

        if (build is not null)
        {
            return await runProcess(CreateRunCommand(build, arguments));
        }

        if (arguments.Count > 0 && string.Equals(arguments[0], "setup", StringComparison.OrdinalIgnoreCase))
        {
            return BuildScaffolder.Run(directory, arguments.Skip(1).ToList(), console, GetBaunVersion());
        }

        console.Error.WriteLine(
            "error: No baun build found. Expected a single-file Build.cs, or a Build.cs next to a project file, " +
            "in the current folder or its 'build' subfolder.");
        console.Error.WriteLine("Run 'baun setup' to create one.");
        return 1;
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
    /// Gets the version of baun to reference in new builds: the version of this tool.
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
