using System.ComponentModel;
using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Baun.Cli;

/// <summary>
/// The kind of build <c>baun init</c> creates.
/// </summary>
internal enum BuildKind
{
    /// <summary>A single <c>Build.cs</c> file in the current folder.</summary>
    SingleFile,

    /// <summary>A build project in a <c>build</c> subfolder.</summary>
    Project,
}

/// <summary>
/// Implements <c>baun init</c>, which creates a new build, and by default bootstrap scripts that run it on machines
/// without the .NET SDK.
/// </summary>
internal sealed class InitCommand(IAnsiConsole console, CliContext context) : AsyncCommand<InitCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--single-file")]
        [Description("Create a single Build.cs file in the current folder.")]
        public bool SingleFile { get; init; }

        [CommandOption("--project")]
        [Description("Create a build project in a 'build' subfolder.")]
        public bool Project { get; init; }

        [CommandOption("--no-bootstrap")]
        [Description("Don't create the build.sh, build.ps1 and build.cmd scripts that install the .NET SDK when it's missing.")]
        public bool NoBootstrap { get; init; }

        public override ValidationResult Validate() =>
            SingleFile && Project
                ? ValidationResult.Error("Use either --single-file or --project, not both.")
                : ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext commandContext, Settings settings, CancellationToken cancellationToken)
    {
        BuildKind kind = settings switch
        {
            { SingleFile: true } => BuildKind.SingleFile,
            { Project: true } => BuildKind.Project,
            _ when console.Profile.Capabilities.Interactive => AskForKind(),
            _ => BuildKind.SingleFile
        };

        var files = kind == BuildKind.SingleFile
            ? CreateSingleFileBuild(context.Directory, context.BaunVersion)
            : CreateProjectBuild(context.Directory, context.BaunVersion);

        // Files that must be executable: the single-file build for its shebang, and the shell script.
        var executables = new List<string>();

        if (kind == BuildKind.SingleFile)
        {
            executables.Add(Path.Combine(context.Directory, "Build.cs"));
        }

        if (!settings.NoBootstrap)
        {
            foreach (var (path, content) in BootstrapScripts.Create(context.Directory))
            {
                files[path] = content;
            }

            executables.Add(Path.Combine(context.Directory, BootstrapScripts.ShellScriptName));
        }

        string? existing = files.Keys.FirstOrDefault(File.Exists);

        if (existing is not null)
        {
            console.MarkupLineInterpolated($"[red bold]error:[/] {Relative(existing)} already exists.");
            return 1;
        }

        foreach ((string path, string content) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content, cancellationToken);
            console.MarkupLineInterpolated($"[green]Created[/] {Relative(path)}");
        }

        if (!settings.NoBootstrap)
        {
            AddToGitIgnore(BootstrapScripts.DotnetFolder);
        }

        await MarkExecutableAsync(executables);

        console.WriteLine();
        console.MarkupLine("Run [green]baun[/] to run the entry step, or [green]baun Compile[/] to run a specific step.");

        if (!settings.NoBootstrap)
        {
            console.MarkupLine("Without the baun tool or the .NET SDK, run [green]./build.sh[/] on Linux and macOS, or [green]build[/] on Windows.");
        }

        return 0;
    }

    /// <summary>
    /// Adds <paramref name="entry"/> to the <c>.gitignore</c> in the build's folder, creating the file if needed.
    /// </summary>
    private void AddToGitIgnore(string entry)
    {
        string path = Path.Combine(context.Directory, ".gitignore");

        if (!File.Exists(path))
        {
            File.WriteAllText(path, entry + "\n");
            console.MarkupLineInterpolated($"[green]Created[/] .gitignore");
            return;
        }

        string content = File.ReadAllText(path);
        string name = entry.Trim('/');
        bool alreadyIgnored = content
            .Split('\n')
            .Select(line => line.Trim().Trim('/'))
            .Contains(name);

        if (alreadyIgnored)
        {
            return;
        }

        string separator = content.Length == 0 || content.EndsWith('\n') ? string.Empty : "\n";
        File.AppendAllText(path, separator + entry + "\n");
        console.MarkupLineInterpolated($"[green]Updated[/] .gitignore");
    }

    /// <summary>
    /// Makes <paramref name="paths"/> executable on disk and, inside a git repository, in git's index, so they stay
    /// executable when committed from any operating system, including Windows.
    /// </summary>
    private async Task MarkExecutableAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            foreach (string path in paths)
            {
                File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
        }

        if (!IsInGitRepository(context.Directory))
        {
            return;
        }

        var relativePaths = paths.Select(Relative).ToList();
        var git = new ProcessStartInfo("git") { WorkingDirectory = context.Directory };

        foreach (string argument in (string[])["add", "--chmod=+x", "--", .. relativePaths])
        {
            git.ArgumentList.Add(argument);
        }

        int exitCode;

        try
        {
            exitCode = await context.RunProcess(git);
        }
        catch (Win32Exception)
        {
            exitCode = -1; // git isn't installed
        }

        string list = string.Join(", ", relativePaths);

        if (exitCode == 0)
        {
            console.MarkupLineInterpolated($"[green]Staged[/] {list} as executable in git");
        }
        else
        {
            string command = $"git add --chmod=+x -- {string.Join(' ', relativePaths)}";
            console.MarkupLineInterpolated($"[yellow]warning:[/] Couldn't mark {list} as executable in git. Run [green]{command}[/] before committing.");
        }
    }

    private static bool IsInGitRepository(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            string git = Path.Combine(current.FullName, ".git");

            if (Directory.Exists(git) || File.Exists(git))
            {
                return true;
            }
        }

        return false;
    }

    private string Relative(string path) => Path.GetRelativePath(context.Directory, path);

    private BuildKind AskForKind() =>
        console.Prompt(
            new SelectionPrompt<BuildKind>()
                .Title("Which kind of build do you want to create?")
                .AddChoices(BuildKind.SingleFile, BuildKind.Project)
                .UseConverter(kind => kind == BuildKind.SingleFile
                    ? "Single file [grey](Build.cs in this folder)[/]"
                    : "Project [grey](build/Build.csproj and build/Build.cs)[/]"));

    private static Dictionary<string, string> CreateSingleFileBuild(string directory, string baunVersion) => new()
    {
        [Path.Combine(directory, "Build.cs")] = $"""
            #!/usr/bin/env dotnet
            #:package Baun@{baunVersion}

            {BuildFileBody}
            """
    };

    private static Dictionary<string, string> CreateProjectBuild(string directory, string baunVersion) => new()
    {
        [Path.Combine(directory, "build", "Build.csproj")] = $"""
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Baun" Version="{baunVersion}" />
              </ItemGroup>

            </Project>

            """,
        [Path.Combine(directory, "build", "Build.cs")] = BuildFileBody,
    };

    private const string BuildFileBody = """
        [Entry(nameof(Test))]
        partial class Build
        {
            Step Clean() => _ => _
                .Executes(() =>
                {
                    Console.WriteLine("Cleaning");
                });

            Step Compile() => _ => _
                .DependsOn(Clean)
                .Executes(() =>
                {
                    Console.WriteLine("Compiling");
                });

            Step Test() => _ => _
                .DependsOn(Compile)
                .Executes(() =>
                {
                    Console.WriteLine("Testing");
                });
        }

        """;
}
