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

    public override Task<int> ExecuteAsync(CommandContext commandContext, Settings settings, CancellationToken cancellationToken) =>
        context.Build is { } build
            ? UpdateBootstrapScriptsAsync(build, settings, cancellationToken)
            : CreateBuildAsync(settings, cancellationToken);

    /// <summary>
    /// Writes the bootstrap scripts of an existing build, and the <c>.editorconfig</c> of a project build, overwriting
    /// existing ones. The build itself is left alone.
    /// </summary>
    private async Task<int> UpdateBootstrapScriptsAsync(BuildLocation build, Settings settings, CancellationToken cancellationToken)
    {
        string buildPath = Relative(build.ProjectFile ?? build.BuildFile);

        if (settings.SingleFile || settings.Project || settings.NoBootstrap)
        {
            console.MarkupLineInterpolated($"[red bold]error:[/] A build already exists ({buildPath}). In an existing build, baun init only updates the bootstrap scripts, so it doesn't take --single-file, --project or --no-bootstrap.");
            return 1;
        }

        // The scripts run Build.cs or build/_build.csproj next to them.
        bool scriptsFindBuild = build.ProjectFile is null
            ? IsSamePath(build.BuildFile, Path.Combine(context.Directory, "Build.cs"))
            : IsSamePath(build.ProjectFile, Path.Combine(context.Directory, "build", "_build.csproj"));

        if (!scriptsFindBuild)
        {
            console.MarkupLineInterpolated($"[red bold]error:[/] The bootstrap scripts run a single-file Build.cs or build/_build.csproj in the folder they're in, but the build is {buildPath}.");
            return 1;
        }

        if (build.ProjectFile is not null)
        {
            await WriteEditorConfigAsync(cancellationToken);
        }

        await WriteBootstrapScriptsAsync(cancellationToken);
        AddToGitIgnore(BootstrapScripts.DotnetFolder);
        await MarkExecutableAsync([Path.Combine(context.Directory, BootstrapScripts.ShellScriptName)]);

        return 0;
    }

    /// <summary>
    /// Writes the bootstrap scripts, overwriting existing ones.
    /// </summary>
    private async Task WriteBootstrapScriptsAsync(CancellationToken cancellationToken)
    {
        foreach ((string path, string content) in BootstrapScripts.Create(context.Directory))
        {
            bool exists = File.Exists(path);
            await File.WriteAllTextAsync(path, content, cancellationToken);
            console.MarkupLineInterpolated($"[green]{(exists ? "Updated" : "Created")}[/] {Relative(path)}");
        }
    }

    private static bool IsSamePath(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new build, and by default its bootstrap scripts. Existing build files are never overwritten, but
    /// existing bootstrap scripts and the <c>.editorconfig</c> of a project build are.
    /// </summary>
    private async Task<int> CreateBuildAsync(Settings settings, CancellationToken cancellationToken)
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

        if (kind == BuildKind.Project)
        {
            await WriteEditorConfigAsync(cancellationToken);
            await AddToSolutionAsync(files.Keys.Single(path => path.EndsWith(".csproj", StringComparison.Ordinal)));
        }

        if (!settings.NoBootstrap)
        {
            await WriteBootstrapScriptsAsync(cancellationToken);
            AddToGitIgnore(BootstrapScripts.DotnetFolder);
            executables.Add(Path.Combine(context.Directory, BootstrapScripts.ShellScriptName));
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
    /// Asks whether to add the new build project to a solution (<c>.sln</c> or <c>.slnx</c>) in the current folder or
    /// its subfolders, and
    /// adds it to the root of the solution with <c>dotnet sln add</c>. Doesn't ask, and doesn't add it, when there is no
    /// solution or baun init can't ask.
    /// </summary>
    private async Task AddToSolutionAsync(string projectFile)
    {
        var solutions = FindSolutions(context.Directory, depth: 0)
            .Select(Relative)
            .OrderBy(path => path.Count(c => c == Path.DirectorySeparatorChar))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (solutions.Count == 0 || !console.Profile.Capabilities.Interactive)
        {
            return;
        }

        string project = Relative(projectFile);
        string? solution = AskForSolution(solutions, project);

        if (solution is null)
        {
            return;
        }

        var dotnet = new ProcessStartInfo("dotnet") { WorkingDirectory = context.Directory };

        foreach (string argument in (string[])["sln", solution, "add", "--in-root", project])
        {
            dotnet.ArgumentList.Add(argument);
        }

        int exitCode;

        try
        {
            exitCode = await context.RunProcess(dotnet);
        }
        catch (Win32Exception)
        {
            exitCode = -1; // dotnet isn't on the PATH
        }

        if (exitCode == 0)
        {
            console.MarkupLineInterpolated($"[green]Added[/] {project} to {solution}");
        }
        else
        {
            string command = $"dotnet sln {solution} add --in-root {project}";
            console.MarkupLineInterpolated($"[yellow]warning:[/] Couldn't add {project} to {solution}. Run [green]{command}[/] to add it.");
        }
    }

    /// <summary>
    /// Finds the solutions in <paramref name="directory"/> and up to <see cref="SolutionSearchDepth"/> levels of
    /// subfolders, skipping hidden folders and build output.
    /// </summary>
    private static IEnumerable<string> FindSolutions(string directory, int depth)
    {
        IEnumerable<string> solutions = Directory.EnumerateFiles(directory)
            .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".sln" or ".slnx");

        if (depth == SolutionSearchDepth)
        {
            return solutions;
        }

        return solutions.Concat(Directory.EnumerateDirectories(directory)
            .Where(path => Path.GetFileName(path) is var name && !name.StartsWith('.') && !_skippedFolders.Contains(name))
            .SelectMany(path => FindSolutions(path, depth + 1)));
    }

    private const int SolutionSearchDepth = 3;

    private static readonly HashSet<string> _skippedFolders = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "node_modules" };

    private string? AskForSolution(List<string> solutions, string project)
    {
        if (solutions.Count == 1)
        {
            return console.Prompt(new ConfirmationPrompt($"Add {project.EscapeMarkup()} to {solutions[0].EscapeMarkup()}?"))
                ? solutions[0]
                : null;
        }

        const string none = "";

        string choice = console.Prompt(
            new SelectionPrompt<string>()
                .Title($"Add {project.EscapeMarkup()} to a solution?")
                .AddChoices([.. solutions, none])
                .UseConverter(solution => solution == none ? "Don't add it" : solution.EscapeMarkup()));

        return choice == none ? null : choice;
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
                    : "Project [grey](build/_build.csproj and build/Build.cs)[/]"));

    private static Dictionary<string, string> CreateSingleFileBuild(string directory, string baunVersion) => new()
    {
        [Path.Combine(directory, "Build.cs")] = $"""
            #!/usr/bin/env dotnet
            #:package Baun@{baunVersion}

            {BuildFileBody}
            """.ReplaceLineEndings("\n")
    };

    private static Dictionary<string, string> CreateProjectBuild(string directory, string baunVersion) => new()
    {
        [Path.Combine(directory, "build", "_build.csproj")] = $"""
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <RootNamespace></RootNamespace>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Baun" Version="{baunVersion}" />
              </ItemGroup>

            </Project>

            """.ReplaceLineEndings("\n"),
        [Path.Combine(directory, "build", "Build.cs")] = BuildFileBody.ReplaceLineEndings("\n"),
    };

    /// <summary>
    /// Writes the <c>.editorconfig</c> of a project build, overwriting an existing one.
    /// </summary>
    private async Task WriteEditorConfigAsync(CancellationToken cancellationToken)
    {
        string path = Path.Combine(context.Directory, "build", ".editorconfig");
        bool exists = File.Exists(path);
        await File.WriteAllTextAsync(path, EditorConfig.ReplaceLineEndings("\n"), cancellationToken);
        console.MarkupLineInterpolated($"[green]{(exists ? "Updated" : "Created")}[/] {Relative(path)}");
    }

    /// <summary>
    /// Steps are written without the default <c>private</c> modifier and as <c>_ => _</c> lambdas, so IDEs shouldn't
    /// ask for the modifier or warn about using the <c>_</c> parameter.
    /// </summary>
    private const string EditorConfig = """
        [*.cs]
        dotnet_style_require_accessibility_modifiers = omit_if_default
        csharp_default_private_modifier = implicit
        resharper_all_underscore_local_parameter_name_highlighting = none

        """;

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
