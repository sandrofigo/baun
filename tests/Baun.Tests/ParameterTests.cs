using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Baun.Tests;

public sealed class ParameterTests : IDisposable
{
    private readonly AbsolutePath root = (AbsolutePath)Directory.CreateTempSubdirectory("baun-parameter-tests-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public async Task Keeps_the_initial_values_without_any_source()
    {
        var (result, build) = await RunAsync([]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Debug", build.Configuration);
        Assert.Null(build.NuGetApiKey);
        Assert.False(build.Verbose);
        Assert.Equal(1, build.Retries);
        Assert.Empty(build.Projects);
        Assert.Equal(["Show"], build.Executed);
    }

    [Fact]
    public async Task Reads_parameters_from_the_command_line()
    {
        var (result, build) = await RunAsync(["--configuration", "Release", "--retries=3", "--target", "arm64", "Show"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Release", build.Configuration);
        Assert.Equal(3, build.Retries);
        Assert.Equal(Platform.Arm64, build.Target);
        Assert.Equal(["Show"], build.Executed);
    }

    [Fact]
    public async Task Reads_parameters_from_environment_variables()
    {
        var (result, build) = await RunAsync([], environment: new() { ["CONFIGURATION"] = "Release", ["NUGET_API_KEY"] = "secret" });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Release", build.Configuration);
        Assert.Equal("secret", build.NuGetApiKey);
    }

    [Fact]
    public async Task Reads_parameters_from_the_parameters_file()
    {
        var (result, build) = await RunAsync([], json: """
            {
                // comments and trailing commas are allowed
                "configuration": "Release",
                "nuGetApiKey": null,
                "Retries": 5,
                "Verbose": true,
                "nuget-source": "https://example.com/feed",
            }
            """);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Release", build.Configuration);
        Assert.Null(build.NuGetApiKey);
        Assert.Equal(5, build.Retries);
        Assert.True(build.Verbose);
        Assert.Equal(new Uri("https://example.com/feed"), build.Source);
    }

    [Theory]
    [InlineData(true, true, "cli")]
    [InlineData(false, true, "env")]
    [InlineData(false, false, "file")]
    public async Task The_command_line_takes_precedence_over_environment_variables_and_both_over_the_file(bool cli, bool env, string expected)
    {
        string[] arguments = cli ? ["--configuration", "cli"] : [];
        Dictionary<string, string> environment = env ? new() { ["Configuration"] = "env" } : [];

        var (result, build) = await RunAsync(arguments, environment, json: """{ "Configuration": "file" }""");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, build.Configuration);
    }

    [Fact]
    public async Task Treats_an_empty_environment_variable_as_not_set()
    {
        var (result, build) = await RunAsync([], new() { ["CONFIGURATION"] = "" }, json: """{ "Configuration": "file" }""");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("file", build.Configuration);
    }

    [Theory]
    [InlineData("--nuget-api-key")]
    [InlineData("--NuGetApiKey")]
    [InlineData("--nu-get-api-key")]
    [InlineData("--NUGET_API_KEY")]
    public async Task Matches_names_ignoring_case_and_separators(string option)
    {
        var (result, build) = await RunAsync([option, "secret"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("secret", build.NuGetApiKey);
    }

    [Theory]
    [InlineData(new[] { "--verbose" }, true, new[] { "Show" })]
    [InlineData(new[] { "--verbose", "false" }, false, new[] { "Show" })]
    [InlineData(new[] { "--verbose", "Test" }, true, new[] { "Test" })]
    [InlineData(new[] { "--verbose=false", "Test" }, false, new[] { "Test" })]
    public async Task A_bool_parameter_alone_means_true(string[] arguments, bool expected, string[] executed)
    {
        var (result, build) = await RunAsync(arguments);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, build.Verbose);
        Assert.Equal(executed, build.Executed);
    }

    [Fact]
    public async Task Reads_arrays_from_a_repeated_option()
    {
        var (result, build) = await RunAsync(["--projects", "src/A", "--platforms", "x64", "--projects", "src/B"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["src/A", "src/B"], build.Projects);
        Assert.Equal([Platform.X64], build.Platforms);
    }

    [Fact]
    public async Task Reads_arrays_from_comma_separated_environment_variables()
    {
        var (result, build) = await RunAsync([], new() { ["PROJECTS"] = " src/A , src/B,,", ["PLATFORMS"] = "x64,ARM64" });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["src/A", "src/B"], build.Projects);
        Assert.Equal([Platform.X64, Platform.Arm64], build.Platforms);
    }

    [Fact]
    public async Task Reads_arrays_from_the_parameters_file()
    {
        var (result, build) = await RunAsync([], json: """{ "Projects": ["src/A", "src/B"], "Platforms": "arm64" }""");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["src/A", "src/B"], build.Projects);
        Assert.Equal([Platform.Arm64], build.Platforms);
    }

    [Fact]
    public async Task Replaces_the_whole_array_from_the_source_with_the_highest_precedence()
    {
        var (result, build) = await RunAsync(["--projects", "cli"], new() { ["PROJECTS"] = "env1,env2" }, """{ "Projects": ["file"] }""");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["cli"], build.Projects);
    }

    [Theory]
    [InlineData(new[] { "--retries", "abc" }, "Parameter Retries: \"abc\" from option --retries is not a valid Int32.")]
    [InlineData(new[] { "--target", "x86" }, "Parameter Target: \"x86\" from option --target is not a valid Platform. Valid values: X64, Arm64.")]
    [InlineData(new[] { "--platforms", "x64", "--platforms", "nope" }, "Parameter Platforms: item 2 \"nope\" from option --platforms is not a valid Platform.")]
    [InlineData(new[] { "--configuration" }, "Option --configuration requires a value.")]
    [InlineData(new[] { "--configuration", "--verbose" }, "Option --configuration requires a value.")]
    [InlineData(new[] { "--configuration", "a", "--configuration", "b" }, "Option --configuration can only be given once.")]
    [InlineData(new[] { "--unknown", "a" }, "Unknown option --unknown.")]
    public async Task Fails_for_invalid_command_line_values(string[] arguments, string error)
    {
        var (result, build) = await RunAsync(arguments);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Contains(error, result.Output);
    }

    [Fact]
    public async Task Fails_for_an_invalid_environment_variable()
    {
        var (result, _) = await RunAsync([], new() { ["retries"] = "many" });

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Parameter Retries: \"many\" from environment variable retries is not a valid Int32.", result.Output);
    }

    [Theory]
    [InlineData("""{ "Configration": "Release" }""", "Unknown parameter Configration in .baun/parameters.json. Available parameters: Configuration,")]
    [InlineData("""{ "Configuration": ["a"] }""", "Parameter Configuration in .baun/parameters.json takes a single value, not an array.")]
    [InlineData("""{ "Configuration": { } }""", "Parameter Configuration in .baun/parameters.json must be a string, number or boolean.")]
    [InlineData("""{ "Projects": [["a"]] }""", "Parameter Projects in .baun/parameters.json must be a string, number or boolean, or an array of them.")]
    [InlineData("""{ "Retries": "x" }""", "Parameter Retries: \"x\" from .baun/parameters.json is not a valid Int32.")]
    [InlineData("""[]""", ".baun/parameters.json must contain a JSON object")]
    [InlineData("""{ "Configuration": """, ".baun/parameters.json is not valid JSON:")]
    public async Task Fails_for_an_invalid_parameters_file(string json, string error)
    {
        var (result, build) = await RunAsync([], json: json);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Contains(error, result.Output);
    }

    [Fact]
    public async Task Fails_for_a_parameters_file_with_values_when_the_build_has_no_parameters()
    {
        var path = WriteParametersFile("""{ "Configuration": "Release" }""");
        var console = new TestConsole().Width(250);

        var exitCode = await new BuildRunner(new NoEntryBuild(), console, new ParameterSources(new Dictionary<string, string>(), path)).RunAsync(["Compile"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown parameter Configuration in .baun/parameters.json. The build has no parameters.", console.Output);
    }

    [Fact]
    public async Task Accepts_an_empty_parameters_file_when_the_build_has_no_parameters()
    {
        var path = WriteParametersFile("{}\n");
        var console = new TestConsole().Width(250);

        var exitCode = await new BuildRunner(new NoEntryBuild(), console, new ParameterSources(new Dictionary<string, string>(), path)).RunAsync(["Compile"]);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Sets_parameters_before_step_definitions_run()
    {
        var (result, build) = await RunAsync(["--configuration", "Release", "Configured"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Configured Release"], build.Executed);
    }

    [Fact]
    public async Task Shows_the_parameters_in_the_help_and_ignores_invalid_values()
    {
        var (result, build) = await RunAsync(["--help"], new() { ["RETRIES"] = "many" });

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Contains("PARAMETERS:", result.Output);
        Assert.Matches(@"--configuration <value>\s+Configuration to build", result.Output);
        Assert.Matches(@"--verbose\s*\n", result.Output);
        Assert.Matches(@"--projects <value>\.\.\.\s+Projects to pack", result.Output);
        Assert.Contains("--nuget-source <value>", result.Output);
        Assert.Contains("--nu-get-api-key <value>", result.Output);
        Assert.Contains("Also read from environment variables, such as CONFIGURATION, and .baun/parameters.json.", result.Output);
    }

    [Fact]
    public async Task Colors_only_the_headings_of_the_help_with_parameters()
    {
        var console = new TestConsole().Width(250).EmitAnsiSequences().Colors(ColorSystem.TrueColor);

        var exitCode = await new BuildRunner(new ParameterBuild(), console, ParameterSources.None).RunAsync(["--help"]);

        Assert.Equal(0, exitCode);
        var colored = Regex.Matches(console.Output, @"\e\[38;[0-9;]+m([^\e]*)\e\[0m").Select(match => match.Groups[1].Value);
        Assert.Equal(["USAGE:", "[steps] [OPTIONS]", "STEPS:", "PARAMETERS:", "OPTIONS:"], colored);
    }

    [Fact]
    public void Finds_the_parameters_file_in_the_nearest_baun_folder_of_a_parent()
    {
        var repository = root / "repo";
        Directory.CreateDirectory(repository / ".baun");
        Directory.CreateDirectory(repository / ".git"); // ignored, only .baun counts
        var subfolder = repository / "src" / "app";
        Directory.CreateDirectory(subfolder);

        Assert.Equal(repository / ".baun" / "parameters.json", ParameterSources.ForDirectory(subfolder).ParametersFile);
    }

    [Fact]
    public void Prefers_the_nearest_baun_folder()
    {
        Directory.CreateDirectory(root / ".baun");
        var nested = root / "nested";
        Directory.CreateDirectory(nested / ".baun");

        Assert.Equal(nested / ".baun" / "parameters.json", ParameterSources.ForDirectory(nested).ParametersFile);
    }

    [Fact]
    public void Finds_the_root_directory_by_the_nearest_baun_folder()
    {
        Directory.CreateDirectory(root / ".baun");
        var subfolder = root / "src" / "app";
        Directory.CreateDirectory(subfolder);

        Assert.Equal(root, BaunBuild.FindRootDirectory(subfolder));
    }

    [Fact]
    public void Uses_the_directory_itself_as_the_root_directory_without_a_baun_folder()
    {
        var subfolder = root / "src";
        Directory.CreateDirectory(subfolder);

        Assert.Equal(subfolder, BaunBuild.FindRootDirectory(subfolder));
    }

    [Fact]
    public void Has_no_parameters_file_without_a_baun_folder()
    {
        Directory.CreateDirectory(root / ".git");

        Assert.Null(ParameterSources.ForDirectory(root).ParametersFile);
    }

    [Fact]
    public void Reads_the_environment_variables_of_the_process()
    {
        string name = "BAUN_TEST_" + Guid.NewGuid().ToString("N");
        System.Environment.SetEnvironmentVariable(name, "value");

        try
        {
            Assert.Equal("value", ParameterSources.ForDirectory(root).Environment[name]);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Theory]
    [InlineData("Configuration", "--configuration")]
    [InlineData("NuGetApiKey", "--nu-get-api-key")]
    [InlineData("APIKey", "--api-key")]
    [InlineData("nuget-source", "--nuget-source")]
    [InlineData("Max_Count", "--max-count")]
    [InlineData("Retries2", "--retries2")]
    public void Converts_names_to_options(string name, string option)
    {
        Assert.Equal(option, ParameterResolver.ToOption(name));
    }

    [Fact]
    public async Task Resolves_relative_paths_against_the_root_directory()
    {
        var absolute = root / "out";

        var (result, build) = await RunAsync(["--output", "artifacts/packages", "--inputs", absolute, "--inputs", "src"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(BaunBuild.RootDirectory / "artifacts" / "packages", build.Output);
        Assert.Equal([absolute, BaunBuild.RootDirectory / "src"], build.Inputs);
    }

    [Fact]
    public async Task Reads_paths_from_the_parameters_file()
    {
        var (result, build) = await RunAsync([], json: """{ "Output": "artifacts" }""");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(BaunBuild.RootDirectory / "artifacts", build.Output);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Treats_an_empty_path_as_null(string value)
    {
        var (cli, cliBuild) = await RunAsync(["--output", value]);
        var (file, fileBuild) = await RunAsync([], json: $$"""{ "Output": "{{value}}" }""");

        Assert.Equal(0, cli.ExitCode);
        Assert.Null(cliBuild.Output);
        Assert.Equal(0, file.ExitCode);
        Assert.Null(fileBuild.Output);
    }

    [Fact]
    public async Task Fails_for_an_empty_path_in_an_array()
    {
        var (result, build) = await RunAsync(["--inputs", "src", "--inputs", ""]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Contains("Parameter Inputs: item 2 \"\" from option --inputs is not a valid AbsolutePath.", result.Output);
    }

    [Fact]
    public async Task Reads_a_solution_and_projects()
    {
        var solution = root / "App.slnx";
        File.WriteAllText(solution, """<Solution><Project Path="src/App/App.csproj" /></Solution>""");
        var project = root / "src" / "App" / "App.csproj";
        project.Parent!.CreateDirectory();
        File.WriteAllText(project, "<Project />");

        var (result, build) = await RunAsync(["--solution", solution, "--project-files", project, "--project-files", "Baun.slnx"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(solution, build.Solution!.Path);
        Assert.Equal(project, build.Solution.GetProject("App").Path);
        Assert.Equal([project, BaunBuild.RootDirectory / "Baun.slnx"], build.ProjectFiles.Select(item => item.Path));
    }

    [Fact]
    public async Task Resolves_a_relative_solution_against_the_root_directory()
    {
        var (result, build) = await RunAsync([], json: """{ "Solution": "Baun.slnx" }""");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(BaunBuild.RootDirectory / "Baun.slnx", build.Solution!.Path);
        Assert.Contains("Baun", build.Solution.GetProjects().Select(project => project.Name));
    }

    [Fact]
    public async Task Treats_an_empty_solution_as_null()
    {
        var (result, build) = await RunAsync(["--solution", ""]);

        Assert.Equal(0, result.ExitCode);
        Assert.Null(build.Solution);
    }

    [Theory]
    [InlineData("--solution", "Missing.slnx", "Parameter Solution: \"Missing.slnx\" from option --solution is not a valid Solution. The solution")]
    [InlineData("--solution", "README.md", "Parameter Solution: \"README.md\" from option --solution is not a valid Solution.")]
    [InlineData("--project-files", "Missing.csproj", "Parameter ProjectFiles: item 1 \"Missing.csproj\" from option --project-files is not a valid Project. The project")]
    public async Task Fails_for_a_missing_or_invalid_solution_or_project(string option, string value, string message)
    {
        var (result, build) = await RunAsync([option, value]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Contains(message, result.Output);
    }

    [Fact]
    public void Registers_parameters_in_declaration_order()
    {
        var parameters = ((IBaunBuild)new ParameterBuild()).GetParameters();

        Assert.Equal(
            ["Configuration", "NuGetApiKey", "Verbose", "Retries", "Target", "Projects", "Platforms", "nuget-source", "Output", "Inputs", "Solution", "ProjectFiles"],
            parameters.Select(parameter => parameter.Name));
        Assert.Equal(typeof(Platform?), parameters[4].Type);
        Assert.Equal("Configuration to build", parameters[0].Description);
        Assert.Null(parameters[1].Description);
    }

    private async Task<(RunResult Result, ParameterBuild Build)> RunAsync(
        string[] arguments,
        Dictionary<string, string>? environment = null,
        string? json = null)
    {
        var build = new ParameterBuild();
        var console = new TestConsole().Width(250);
        var sources = new ParameterSources(environment ?? [], json is null ? null : WriteParametersFile(json));

        var exitCode = await new BuildRunner(build, console, sources).RunAsync(arguments);

        return (new RunResult(exitCode, console.Output), build);
    }

    private AbsolutePath WriteParametersFile(string json)
    {
        var path = root / ".baun" / "parameters.json";
        Directory.CreateDirectory(path.Parent!);
        File.WriteAllText(path, json);
        return path;
    }

    private sealed record RunResult(int ExitCode, string Output);
}

internal enum Platform
{
    X64,
    Arm64,
}

[Entry(nameof(Show))]
internal sealed partial class ParameterBuild
{
    [Parameter("Configuration to build")]
    public string Configuration = "Debug";

    [Parameter]
    public string? NuGetApiKey { get; private set; }

    [Parameter]
    public bool Verbose;

    [Parameter]
    public int Retries = 1;

    [Parameter]
    public Platform? Target;

    [Parameter("Projects to pack")]
    public string[] Projects = [];

    [Parameter]
    public Platform[] Platforms = [];

    [Parameter(Name = "nuget-source")]
    public Uri Source = new("https://api.nuget.org/v3/index.json");

    [Parameter]
    public AbsolutePath? Output;

    [Parameter]
    public AbsolutePath[] Inputs = [];

    [Parameter]
    public Baun.DotNet.Solution? Solution;

    [Parameter]
    public Baun.DotNet.Project[] ProjectFiles = [];

    public List<string> Executed { get; } = [];

    public Step Show() => _ => _
        .Executes(() => Executed.Add(nameof(Show)));

    public Step Test() => _ => _
        .Executes(() => Executed.Add(nameof(Test)));

    // Reads a parameter in the step definition, not only when the step runs.
    public Step Configured()
    {
        string name = $"Configured {Configuration}";
        return _ => _.Executes(() => Executed.Add(name));
    }
}
