namespace BaunCli;

/// <summary>
/// The kind of build <c>baun setup</c> creates.
/// </summary>
internal enum BuildKind
{
    /// <summary>A single <c>Build.cs</c> file in the current folder.</summary>
    SingleFile,

    /// <summary>A build project in a <c>build</c> subfolder.</summary>
    Project,
}

/// <summary>
/// Implements <c>baun setup</c>, which creates a new build.
/// </summary>
internal static class BuildScaffolder
{
    private const string SingleFileOption = "--single-file";
    private const string ProjectOption = "--project";

    /// <summary>
    /// Creates a new build in <paramref name="directory"/>.
    /// </summary>
    /// <param name="directory">The folder to create the build in.</param>
    /// <param name="options">The options after <c>setup</c>: <c>--single-file</c> or <c>--project</c>.</param>
    /// <param name="console">The console to use. If it is interactive and no option is given, the user is asked.</param>
    /// <param name="baunVersion">The version of the Baun package the new build references.</param>
    /// <returns>The process exit code.</returns>
    public static int Run(string directory, IReadOnlyList<string> options, CliConsole console, string baunVersion)
    {
        BuildKind? kind = null;

        foreach (var option in options)
        {
            BuildKind selected;

            if (option == SingleFileOption)
            {
                selected = BuildKind.SingleFile;
            }
            else if (option == ProjectOption)
            {
                selected = BuildKind.Project;
            }
            else
            {
                console.Error.WriteLine($"error: Unknown option {option}. Use {SingleFileOption} or {ProjectOption}.");
                return 1;
            }

            if (kind is not null && kind != selected)
            {
                console.Error.WriteLine($"error: Use either {SingleFileOption} or {ProjectOption}, not both.");
                return 1;
            }

            kind = selected;
        }

        kind ??= console.IsInteractive ? AskForKind(console) : BuildKind.SingleFile;

        var files = kind == BuildKind.SingleFile
            ? CreateSingleFileBuild(directory, baunVersion)
            : CreateProjectBuild(directory, baunVersion);

        var existing = files.Keys.Where(File.Exists).ToList();

        if (existing.Count > 0)
        {
            console.Error.WriteLine($"error: {Path.GetRelativePath(directory, existing[0])} already exists.");
            return 1;
        }

        foreach (var (path, content) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            console.Out.WriteLine($"Created {Path.GetRelativePath(directory, path)}");
        }

        if (kind == BuildKind.SingleFile && !OperatingSystem.IsWindows())
        {
            // Lets the build run directly through its shebang, for example "./Build.cs Test".
            var buildFile = files.Keys.Single();
            File.SetUnixFileMode(buildFile, File.GetUnixFileMode(buildFile) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }

        console.Out.WriteLine();
        console.Out.WriteLine("Run 'baun' to run the entry step, or 'baun Compile' to run a specific step.");
        return 0;
    }

    private static BuildKind AskForKind(CliConsole console)
    {
        console.Out.WriteLine("Which kind of build do you want to create?");
        console.Out.WriteLine("  1) Single file: Build.cs in this folder (default)");
        console.Out.WriteLine("  2) Project: build/Build.csproj and build/Build.cs");

        while (true)
        {
            console.Out.Write("> ");
            var answer = console.In.ReadLine()?.Trim();

            switch (answer)
            {
                case null or "" or "1":
                    return BuildKind.SingleFile;
                case "2":
                    return BuildKind.Project;
                default:
                    console.Out.WriteLine("Please enter 1 or 2.");
                    break;
            }
        }
    }

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
