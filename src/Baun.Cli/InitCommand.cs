using System.ComponentModel;
using System.Diagnostics;
using System.Text;
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
    /// Writes the bootstrap scripts of an existing build, and the IDE settings of a project build, overwriting
    /// existing ones, and creates <c>.baun/parameters.json</c> if it's missing. The build itself is left alone.
    /// </summary>
    private async Task<int> UpdateBootstrapScriptsAsync(BuildLocation build, Settings settings, CancellationToken cancellationToken)
    {
        string buildPath = Relative(build.ProjectFile ?? build.BuildFile);

        if (settings.SingleFile || settings.Project || settings.NoBootstrap)
        {
            console.WriteError(Markup.Escape($"A build already exists ({buildPath}). In an existing build, baun init only updates the bootstrap scripts, so it doesn't take --single-file, --project or --no-bootstrap."));
            return 1;
        }

        // The scripts run Build.cs or build/_build.csproj next to them.
        bool scriptsFindBuild = build.ProjectFile is null
            ? build.BuildFile == context.Directory / "Build.cs"
            : build.ProjectFile == context.Directory / "build" / "_build.csproj";

        if (!scriptsFindBuild)
        {
            console.WriteError(Markup.Escape($"The bootstrap scripts run a single-file Build.cs or build/_build.csproj in the folder they're in, but the build is {buildPath}."));
            return 1;
        }

        if (build.ProjectFile is not null)
        {
            await WriteIdeSettingsAsync(cancellationToken);
        }

        await WriteBootstrapScriptsAsync(cancellationToken);
        AddToGitIgnore(context.Directory / ".gitignore", BootstrapScripts.DotnetFolder);
        await CreateParametersFileAsync(cancellationToken);
        await MarkExecutableAsync([context.Directory / BootstrapScripts.ShellScriptName]);

        return 0;
    }

    /// <summary>
    /// Creates an empty <c>.baun/parameters.json</c> unless it already exists: in the nearest <c>.baun</c> folder of the
    /// current folder or its parents, where the build reads it, or in a new <c>.baun</c> folder in the current folder.
    /// It holds the user's parameter values, so it's never overwritten. Also ignores the temp folder of baun in
    /// <c>.baun/.gitignore</c>.
    /// </summary>
    private async Task CreateParametersFileAsync(CancellationToken cancellationToken)
    {
        AbsolutePath root = RepositoryRoot.TryFind(context.Directory) ?? context.Directory;
        AbsolutePath folder = root / RepositoryRoot.FolderName;
        AbsolutePath path = folder / RepositoryRoot.ParametersFileName;

        Directory.CreateDirectory(folder);

        if (!File.Exists(path))
        {
            await File.WriteAllTextAsync(path, "{}\n", cancellationToken);
            console.WriteSuccess($"Created {Markup.Escape(Relative(path))}");
        }

        AddToGitIgnore(folder / ".gitignore", RepositoryRoot.TempFolderName + "/");
    }

    /// <summary>
    /// Writes the bootstrap scripts, overwriting existing ones.
    /// </summary>
    private async Task WriteBootstrapScriptsAsync(CancellationToken cancellationToken)
    {
        foreach ((AbsolutePath path, string content) in BootstrapScripts.Create(context.Directory))
        {
            bool exists = File.Exists(path);
            await File.WriteAllTextAsync(path, content, cancellationToken);
            console.WriteSuccess($"{(exists ? "Updated" : "Created")} {Markup.Escape(Relative(path))}");
        }
    }

    /// <summary>
    /// Creates a new build, <c>.baun/parameters.json</c> and by default its bootstrap scripts. Existing build files and
    /// parameters are never overwritten, but existing bootstrap scripts and the IDE settings of a project
    /// build are.
    /// </summary>
    private async Task<int> CreateBuildAsync(Settings settings, CancellationToken cancellationToken)
    {
        BuildKind kind = settings switch
        {
            { SingleFile: true } => BuildKind.SingleFile,
            { Project: true } => BuildKind.Project,
            _ when console.Profile.Capabilities.Interactive => AskForKind(),
            _ => BuildKind.Project
        };

        var files = kind == BuildKind.SingleFile
            ? CreateSingleFileBuild(context.Directory, context.BaunVersion)
            : CreateProjectBuild(context.Directory, context.BaunVersion);

        // Files that must be executable: the single-file build for its shebang, and the shell script.
        var executables = new List<AbsolutePath>();

        if (kind == BuildKind.SingleFile)
        {
            executables.Add(context.Directory / "Build.cs");
        }

        AbsolutePath? existing = files.Keys.FirstOrDefault(path => File.Exists(path));

        if (existing is not null)
        {
            console.WriteError(Markup.Escape($"{Relative(existing)} already exists."));
            return 1;
        }

        foreach ((AbsolutePath path, string content) in files)
        {
            Directory.CreateDirectory(path.Parent!);
            await File.WriteAllTextAsync(path, content, cancellationToken);
            console.WriteSuccess($"Created {Markup.Escape(Relative(path))}");
        }

        if (kind == BuildKind.Project)
        {
            await CreateDirectoryBuildFilesAsync(cancellationToken);
            await WriteIdeSettingsAsync(cancellationToken);
            await AddToSolutionAsync(files.Keys.Single(path => path.Extension == ".csproj"));
        }

        if (!settings.NoBootstrap)
        {
            await WriteBootstrapScriptsAsync(cancellationToken);
            AddToGitIgnore(context.Directory / ".gitignore", BootstrapScripts.DotnetFolder);
            executables.Add(context.Directory / BootstrapScripts.ShellScriptName);
        }

        await CreateParametersFileAsync(cancellationToken);
        await MarkExecutableAsync(executables);

        WriteNextSteps(bootstrap: !settings.NoBootstrap);

        return 0;
    }

    /// <summary>
    /// Writes how to run the new build.
    /// </summary>
    private void WriteNextSteps(bool bootstrap)
    {
        Grid grid = new Grid()
            .AddColumn(new GridColumn().PadLeft(2).PadRight(2))
            .AddColumn()
            .AddRow(Theme.Accent("baun"), Theme.Faint("Run the entry step"))
            .AddRow(Theme.Accent("baun Compile"), Theme.Faint("Run a specific step"));

        if (bootstrap)
        {
            grid.AddRow(Theme.Accent("./build.sh"), Theme.Faint("Run without the baun tool or the .NET SDK on Linux and macOS"));
            grid.AddRow(Theme.Accent("./build.cmd"), Theme.Faint("Run without the baun tool or the .NET SDK on Windows"));
        }

        console.WriteLine();
        console.MarkupLine("Next steps:");
        console.Write(grid);
    }

    /// <summary>
    /// Adds <paramref name="entry"/> to the <c>.gitignore</c> at <paramref name="path"/>, creating the file if needed.
    /// </summary>
    private void AddToGitIgnore(AbsolutePath path, string entry)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, entry + "\n");
            console.WriteSuccess($"Created {Markup.Escape(Relative(path))}");
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
        console.WriteSuccess($"Updated {Markup.Escape(Relative(path))}");
    }

    /// <summary>
    /// Asks whether to add the new build project to a solution (<c>.sln</c> or <c>.slnx</c>) in the current folder or
    /// its subfolders, and
    /// adds it to the root of the solution with <c>dotnet sln add</c>. Doesn't ask, and doesn't add it, when there is no
    /// solution or baun init can't ask.
    /// </summary>
    private async Task AddToSolutionAsync(AbsolutePath projectFile)
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
            console.WriteSuccess($"Added {Markup.Escape(project)} to {Markup.Escape(solution)}");
        }
        else
        {
            string command = $"dotnet sln {solution} add --in-root {project}";
            console.WriteWarning($"Couldn't add {Markup.Escape(project)} to {Markup.Escape(solution)}. Run {Theme.Accent(command)} to add it.");
        }
    }

    /// <summary>
    /// Finds the solutions in <paramref name="directory"/> and up to <see cref="SolutionSearchDepth"/> levels of
    /// subfolders, skipping hidden folders and build output.
    /// </summary>
    private static IEnumerable<AbsolutePath> FindSolutions(AbsolutePath directory, int depth)
    {
        IEnumerable<AbsolutePath> solutions = Directory.EnumerateFiles(directory)
            .Select(path => (AbsolutePath)path)
            .Where(path => path.Extension.ToLowerInvariant() is ".sln" or ".slnx");

        if (depth == SolutionSearchDepth)
        {
            return solutions;
        }

        return solutions.Concat(Directory.EnumerateDirectories(directory)
            .Select(path => (AbsolutePath)path)
            .Where(path => !path.Name.StartsWith('.') && !_skippedFolders.Contains(path.Name))
            .SelectMany(path => FindSolutions(path, depth + 1)));
    }

    private const int SolutionSearchDepth = 3;

    private static readonly HashSet<string> _skippedFolders = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "node_modules" };

    private string? AskForSolution(List<string> solutions, string project)
    {
        if (solutions.Count == 1)
        {
            var confirmation = new ConfirmationPrompt($"Add {Theme.Accent(project)} to {Theme.Accent(solutions[0])}?")
            {
                ChoicesStyle = Theme.PrimaryStyle,
                DefaultValueStyle = Theme.FaintStyle,
            };

            return console.Prompt(confirmation) ? solutions[0] : null;
        }

        const string none = "";

        string choice = Select(
            $"Add {project} to a solution?",
            [.. solutions, none],
            solution => solution == none ? "Don't add it" : solution);

        return choice == none ? null : choice;
    }

    /// <summary>
    /// Asks to pick one of <paramref name="choices"/>, then writes the question and the answer, since the prompt
    /// disappears once answered.
    /// </summary>
    /// <param name="title">The question.</param>
    /// <param name="choices">The choices.</param>
    /// <param name="label">Returns a choice's label.</param>
    /// <param name="hint">Returns a faint hint shown after a choice's label in the prompt, or <see langword="null"/>.</param>
    /// <param name="defaultValue">The default value to select.</param>
    private T Select<T>(string title, IEnumerable<T> choices, Func<T, string> label, Func<T, string?>? hint = null, T? defaultValue = default)
        where T : notnull
    {
        T choice = console.Prompt(
            new SelectionPrompt<T>()
                .Title(Markup.Escape(title))
                .HighlightStyle(Theme.PrimaryStyle)
                .AddChoices(choices)
                .DefaultValue(defaultValue)
                .UseConverter(item => Markup.Escape(label(item)) + (hint?.Invoke(item) is { } text ? " " + Theme.Faint(text) : "")));

        console.WriteSuccess($"{Markup.Escape(title)} {Theme.Faint(console.Chevron())} {Theme.Accent(label(choice))}");
        return choice;
    }

    /// <summary>
    /// Makes <paramref name="paths"/> executable on disk and, inside a git repository, in git's index, so they stay
    /// executable when committed from any operating system, including Windows.
    /// </summary>
    private async Task MarkExecutableAsync(IReadOnlyList<AbsolutePath> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            foreach (AbsolutePath path in paths)
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
            console.WriteSuccess($"Staged {Markup.Escape(list)} as executable in git");
        }
        else
        {
            string command = $"git add --chmod=+x -- {string.Join(' ', relativePaths)}";
            console.WriteWarning($"Couldn't mark {Markup.Escape(list)} as executable in git. Run {Theme.Accent(command)} before committing.");
        }
    }

    private static bool IsInGitRepository(AbsolutePath directory)
    {
        for (AbsolutePath? current = directory; current is not null; current = current.Parent)
        {
            AbsolutePath git = current / ".git";

            if (Directory.Exists(git) || File.Exists(git))
            {
                return true;
            }
        }

        return false;
    }

    private string Relative(AbsolutePath path) => Path.GetRelativePath(context.Directory, path);

    private BuildKind AskForKind() =>
        Select(
            "Which kind of build do you want to create?",
            [BuildKind.Project, BuildKind.SingleFile],
            kind => kind == BuildKind.SingleFile ? "Single file" : "Project",
            kind => kind == BuildKind.SingleFile ? "Build.cs in this folder" : "build/_build.csproj and build/Build.cs",
            BuildKind.Project);

    private static Dictionary<AbsolutePath, string> CreateSingleFileBuild(AbsolutePath directory, string baunVersion) => new()
    {
        [directory / "Build.cs"] = $"""
            #!/usr/bin/env dotnet
            #:package Baun@{baunVersion}

            {BuildFileBody}
            """.ReplaceLineEndings("\n")
    };

    private static Dictionary<AbsolutePath, string> CreateProjectBuild(AbsolutePath directory, string baunVersion) => new()
    {
        [directory / "build" / "_build.csproj"] = $"""
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <RootNamespace></RootNamespace>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Baun" Version="{baunVersion}" />
              </ItemGroup>

            </Project>

            """.ReplaceLineEndings("\n"),
        [directory / "build" / "Build.cs"] = BuildFileBody.ReplaceLineEndings("\n"),
    };

    /// <summary>
    /// Creates an empty <c>Directory.Build.props</c> and <c>Directory.Build.targets</c> in the <c>build</c> folder
    /// unless they exist, so the build project doesn't import the ones of the repository. Their settings, such as
    /// <c>TreatWarningsAsErrors</c>, analyzers or package metadata, are meant for the repository's own projects. The
    /// files are never overwritten, since the build can use them for its own settings.
    /// </summary>
    private async Task CreateDirectoryBuildFilesAsync(CancellationToken cancellationToken)
    {
        foreach (string name in (string[])["Directory.Build.props", "Directory.Build.targets"])
        {
            AbsolutePath path = context.Directory / "build" / name;

            if (File.Exists(path))
            {
                continue;
            }

            await File.WriteAllTextAsync(path, DirectoryBuildFile.ReplaceLineEndings("\n"), cancellationToken);
            console.WriteSuccess($"Created {Markup.Escape(Relative(path))}");
        }
    }

    private const string DirectoryBuildFile = """
        <Project>

          <!-- Keeps the build from importing the file of the same name in the folders above it. -->

        </Project>

        """;

    /// <summary>
    /// Writes the <c>.editorconfig</c> and <c>_build.csproj.DotSettings</c> of a project build, overwriting existing ones.
    /// </summary>
    private async Task WriteIdeSettingsAsync(CancellationToken cancellationToken)
    {
        // Rider saves .DotSettings files with a BOM, and rewrites one without it when it opens the project.
        var files = new[]
        {
            (".editorconfig", EditorConfig, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)),
            ("_build.csproj.DotSettings", DotSettings, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)),
        };

        foreach ((string name, string content, Encoding encoding) in files)
        {
            AbsolutePath path = context.Directory / "build" / name;
            bool exists = File.Exists(path);
            await File.WriteAllTextAsync(path, content.ReplaceLineEndings("\n"), encoding, cancellationToken);
            console.WriteSuccess($"{(exists ? "Updated" : "Created")} {Markup.Escape(Relative(path))}");
        }
    }

    /// <summary>
    /// Steps are written without the default <c>private</c> modifier and as <c>_ => _</c> lambdas, and parameters as
    /// PascalCase fields, so IDEs shouldn't ask for the modifier or warn about the <c>_</c> parameter or the parameter
    /// names.
    /// </summary>
    private const string EditorConfig = """
        [*.cs]
        dotnet_style_require_accessibility_modifiers = omit_if_default
        csharp_default_private_modifier = implicit
        resharper_all_underscore_local_parameter_name_highlighting = none
        resharper_csharp_naming_rule.private_instance_fields = AaBb, _+aaBb, aaBb

        """;

    /// <summary>
    /// The settings of <see cref="EditorConfig"/> for ReSharper and Rider, which don't read <c>.editorconfig</c> when
    /// it's turned off in their options. It's in the exact form Rider saves it in, including the flag that the naming
    /// rules are already migrated, so Rider doesn't rewrite it when it opens the project.
    /// </summary>
    private const string DotSettings = """
        <wpf:ResourceDictionary xml:space="preserve" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:s="clr-namespace:System;assembly=mscorlib" xmlns:ss="urn:shemas-jetbrains-com:settings-storage-xaml" xmlns:wpf="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
        	<s:String x:Key="/Default/CodeInspection/Highlighting/InspectionSeverities/=AllUnderscoreLocalParameterName/@EntryIndexedValue">DO_NOT_SHOW</s:String>
        	<s:String x:Key="/Default/CodeStyle/CodeFormatting/CSharpCodeStyle/DEFAULT_INTERNAL_MODIFIER/@EntryValue">Implicit</s:String>
        	<s:String x:Key="/Default/CodeStyle/CodeFormatting/CSharpCodeStyle/DEFAULT_PRIVATE_MODIFIER/@EntryValue">Implicit</s:String>
        	<s:String x:Key="/Default/CodeStyle/Naming/CSharpNaming/PredefinedNamingRules/=PrivateInstanceFields/@EntryIndexedValue">&lt;Policy Inspect="True" Prefix="" Suffix="" Style="AaBb"&gt;&lt;ExtraRule Prefix="_" Suffix="" Style="aaBb" /&gt;&lt;ExtraRule Prefix="" Suffix="" Style="aaBb" /&gt;&lt;/Policy&gt;</s:String>
        	<s:String x:Key="/Default/CodeStyle/Naming/CSharpNaming/UserRules/=4a98fdf6_002D7d98_002D4f5a_002Dafeb_002Dea44ad98c70c/@EntryIndexedValue">&lt;Policy&gt;&lt;Descriptor Staticness="Instance" AccessRightKinds="Private" Description="Instance fields (private)"&gt;&lt;ElementKinds&gt;&lt;Kind Name="FIELD" /&gt;&lt;Kind Name="READONLY_FIELD" /&gt;&lt;/ElementKinds&gt;&lt;/Descriptor&gt;&lt;Policy Inspect="True" WarnAboutPrefixesAndSuffixes="False" Prefix="" Suffix="" Style="AaBb"&gt;&lt;ExtraRule Prefix="_" Suffix="" Style="aaBb" /&gt;&lt;ExtraRule Prefix="" Suffix="" Style="aaBb" /&gt;&lt;/Policy&gt;&lt;/Policy&gt;</s:String>
        	<s:Boolean x:Key="/Default/Environment/SettingsMigration/IsMigratorApplied/=JetBrains_002EReSharper_002EPsi_002ECSharp_002ECodeStyle_002ESettingsUpgrade_002EPredefinedNamingRulesToUserRulesUpgrade/@EntryIndexedValue">True</s:Boolean>
        </wpf:ResourceDictionary>

        """;

    private const string BuildFileBody = """
        [Entry(nameof(Test))]
        partial class Build
        {
            Step Clean() => _ => _
                .Executes(() =>
                {
                    Log.Information("Cleaning");
                });

            Step Compile() => _ => _
                .DependsOn(Clean)
                .Executes(() =>
                {
                    Log.Information("Compiling");
                });

            Step Test() => _ => _
                .DependsOn(Compile)
                .Executes(() =>
                {
                    Log.Information("Testing");
                });
        }

        """;
}
