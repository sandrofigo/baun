using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BaunCli;

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
/// Implements <c>baun init</c>, which creates a new build.
/// </summary>
internal sealed class InitCommand(IAnsiConsole console, CliContext context) : Command<InitCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--single-file")]
        [Description("Create a single Build.cs file in the current folder.")]
        public bool SingleFile { get; init; }

        [CommandOption("--project")]
        [Description("Create a build project in a 'build' subfolder.")]
        public bool Project { get; init; }

        public override ValidationResult Validate() =>
            SingleFile && Project
                ? ValidationResult.Error("Use either --single-file or --project, not both.")
                : ValidationResult.Success();
    }

    public override int Execute(CommandContext commandContext, Settings settings, CancellationToken cancellationToken)
    {
        var kind = settings switch
        {
            { SingleFile: true } => BuildKind.SingleFile,
            { Project: true } => BuildKind.Project,
            _ when console.Profile.Capabilities.Interactive => AskForKind(),
            _ => BuildKind.SingleFile,
        };

        var files = kind == BuildKind.SingleFile
            ? CreateSingleFileBuild(context.Directory, context.BaunVersion)
            : CreateProjectBuild(context.Directory, context.BaunVersion);

        var existing = files.Keys.FirstOrDefault(File.Exists);

        if (existing is not null)
        {
            console.MarkupLineInterpolated($"[red bold]error:[/] {Path.GetRelativePath(context.Directory, existing)} already exists.");
            return 1;
        }

        foreach (var (path, content) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            console.MarkupLineInterpolated($"[green]Created[/] {Path.GetRelativePath(context.Directory, path)}");
        }

        if (kind == BuildKind.SingleFile && !OperatingSystem.IsWindows())
        {
            // Lets the build run directly through its shebang, for example "./Build.cs Test".
            var buildFile = files.Keys.Single();
            File.SetUnixFileMode(buildFile, File.GetUnixFileMode(buildFile) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }

        console.WriteLine();
        console.MarkupLine("Run [green]baun[/] to run the entry step, or [green]baun Compile[/] to run a specific step.");
        return 0;
    }

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
            """,
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
        await Baun.RunAsync<Build>();

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

            [Entry]
            Step Test() => _ => _
                .DependsOn(Compile)
                .Executes(() =>
                {
                    Console.WriteLine("Testing");
                });
        }

        """;
}
