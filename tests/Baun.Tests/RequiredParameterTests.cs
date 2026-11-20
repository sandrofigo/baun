using Spectre.Console.Testing;

namespace Baun.Tests;

public sealed class RequiredParameterTests : IDisposable
{
    private const string RequiredOptions = """{ "Configuration": "Release", "Output": "artifacts" }""";

    private readonly AbsolutePath root = (AbsolutePath)Directory.CreateTempSubdirectory("baun-required-tests-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Theory]
    [InlineData("cli")]
    [InlineData("env")]
    [InlineData("file")]
    public async Task Runs_when_the_required_parameters_are_set(string source)
    {
        var (result, build) = source switch
        {
            "cli" => await RunAsync(["--configuration", "Release", "--output", "artifacts"]),
            "env" => await RunAsync([], new() { ["CONFIGURATION"] = "Release", ["OUTPUT"] = "artifacts" }),
            _ => await RunAsync([], json: RequiredOptions),
        };

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Release", build.Configuration);
        Assert.Equal(BaunBuild.RootDirectory / "artifacts", build.Output);
        Assert.Equal(["Test"], build.Executed);
    }

    [Fact]
    public async Task Fails_before_any_step_and_names_every_missing_required_parameter()
    {
        var (result, build) = await RunAsync([]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Contains("Parameter Configuration is required. Pass --configuration, set CONFIGURATION or add it to .baun/parameters.json.", result.Output);
        Assert.Contains("Parameter Output is required. Pass --output, set OUTPUT or add it to .baun/parameters.json.", result.Output);
    }

    [Fact]
    public async Task Treats_empty_values_of_required_parameters_as_missing()
    {
        var (result, build) = await RunAsync(["--configuration", "", "--output", " "]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Contains("Parameter Configuration is required.", result.Output);
        Assert.Contains("Parameter Output is required.", result.Output);
    }

    [Fact]
    public async Task Shows_the_help_while_required_parameters_are_missing()
    {
        var (result, build) = await RunAsync(["--help"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Matches(@"--configuration <value>\s+Configuration to build \(required\)", result.Output);
        Assert.Matches(@"--output <value>\s+\(required\)", result.Output);
        Assert.Matches(@"\n\s+Test\s+Runs when no step is named\s*\n", result.Output);
        Assert.Matches(@"\n\s+Publish\s+Requires --nuget-api-key, --feeds\s*\n", result.Output);
        Assert.Matches(@"\n\s+Mirror\s+Requires Fallback \?\? NuGetApiKey\s*\n", result.Output);
        Assert.Matches(@"\n\s+SignPackages\s+Requires --sign\s*\n", result.Output);
        Assert.Matches(@"\n\s+Upload\s+Requires Feeds\.Length > 0\s*\n", result.Output);
    }

    [Fact]
    public async Task Checks_the_requirements_of_a_step_only_when_it_runs()
    {
        var (result, build) = await RunAsync(["Pack"], json: RequiredOptions);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Test", "Pack"], build.Executed);
    }

    [Fact]
    public async Task Fails_before_any_step_when_a_step_of_the_plan_requires_a_missing_value()
    {
        // Release only depends on Publish, whose requirements are still checked before Test runs.
        var (result, build) = await RunAsync(["Release"], json: RequiredOptions);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Contains("Step Publish requires parameter nuget-api-key. Pass --nuget-api-key, set NUGET_API_KEY or add it to .baun/parameters.json.", result.Output);
        Assert.Contains("Step Publish requires parameter Feeds. Pass --feeds, set FEEDS or add it to .baun/parameters.json.", result.Output);
    }

    [Fact]
    public async Task Runs_a_step_whose_requirements_are_set()
    {
        var (result, build) = await RunAsync(["Release", "--nuget-api-key", "secret", "--feeds", "main"], json: RequiredOptions);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Test", "Pack", "Publish", "Release"], build.Executed);
    }

    [Fact]
    public async Task Names_a_required_expression_that_is_not_a_parameter()
    {
        var (result, build) = await RunAsync(["Mirror"], json: RequiredOptions);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Contains("Step Mirror requires Fallback ?? NuGetApiKey, which isn't set.", result.Output);
    }

    [Fact]
    public async Task Reports_a_requirement_that_throws_as_an_error()
    {
        var (result, build) = await RunAsync(["Inspect"], json: RequiredOptions);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Contains("Step Inspect requires Fallback!.Length, which failed:", result.Output);
    }

    [Theory]
    [InlineData]
    [InlineData("--sign", "false")]
    public async Task Fails_when_a_required_flag_is_false(params string[] arguments)
    {
        var (result, build) = await RunAsync(["SignPackages", .. arguments], json: RequiredOptions);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Contains("Step SignPackages requires parameter Sign. Pass --sign, set SIGN or add it to .baun/parameters.json.", result.Output);
    }

    [Theory]
    [InlineData("--sign")]
    [InlineData("--sign", "true")]
    public async Task Runs_when_a_required_flag_is_true(params string[] arguments)
    {
        var (result, build) = await RunAsync(["SignPackages", .. arguments], json: RequiredOptions);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["SignPackages"], build.Executed);
    }

    [Fact]
    public async Task Fails_when_a_required_condition_is_false()
    {
        var (result, build) = await RunAsync(["Upload"], json: RequiredOptions);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Contains("Step Upload requires Feeds.Length > 0, which is false.", result.Output);
    }

    [Fact]
    public async Task Runs_when_a_required_condition_is_true()
    {
        var (result, build) = await RunAsync(["Upload", "--feeds", "main"], json: RequiredOptions);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Upload"], build.Executed);
    }

    [Theory]
    [InlineData(null, "isn't set")]
    [InlineData(false, "is false")]
    public async Task Fails_when_a_required_nullable_bool_is_not_true(bool? approved, string reason)
    {
        var (result, build) = await RunAsync(["Deploy"], json: RequiredOptions, configure: build => build.Approved = approved);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Contains($"Step Deploy requires Approved, which {reason}.", result.Output);
    }

    [Fact]
    public async Task Runs_when_a_required_nullable_bool_is_true()
    {
        var (result, build) = await RunAsync(["Deploy"], json: RequiredOptions, configure: build => build.Approved = true);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Deploy"], build.Executed);
    }

    [Theory]
    [InlineData("() => NuGetApiKey", "NuGetApiKey")]
    [InlineData("()=>this.NuGetApiKey!", "NuGetApiKey")]
    [InlineData("() => @class", "class")]
    [InlineData("() => NuGetApiKey ?? Fallback", null)]
    [InlineData("() => Settings.NuGetApiKey", null)]
    [InlineData("GetKey", null)]
    public void Finds_the_member_a_requirement_reads(string expression, string? memberName)
    {
        Assert.Equal(memberName, new StepRequirement(() => null, expression).MemberName);
    }

    private async Task<(RunResult Result, RequiredParameterBuild Build)> RunAsync(
        string[] arguments,
        Dictionary<string, string>? environment = null,
        string? json = null,
        Action<RequiredParameterBuild>? configure = null)
    {
        var build = new RequiredParameterBuild();
        configure?.Invoke(build);
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

[Entry(nameof(Test))]
internal sealed partial class RequiredParameterBuild
{
    [Parameter("Configuration to build")]
    public string Configuration;

    [Parameter]
    public AbsolutePath Output { get; set; }

    [Parameter(Name = "nuget-api-key")]
    public string? NuGetApiKey;

    [Parameter]
    public string[] Feeds = [];

    [Parameter]
    public bool Sign;

    public string? Fallback { get; set; }

    public bool? Approved { get; set; }

    public List<string> Executed { get; } = [];

    public Step Test() => _ => _
        .Executes(() => Executed.Add(nameof(Test)));

    public Step Pack() => _ => _
        .DependsOn(Test)
        .Executes(() => Executed.Add(nameof(Pack)));

    public Step Publish() => _ => _
        .DependsOn(Pack)
        .Requires(() => NuGetApiKey)
        .Requires(() => this.Feeds)
        .Executes(() => Executed.Add(nameof(Publish)));

    public Step Release() => _ => _
        .DependsOn(Publish)
        .Executes(() => Executed.Add(nameof(Release)));

    public Step Inspect() => _ => _
        .Requires(() => Fallback!.Length)
        .Executes(() => Executed.Add(nameof(Inspect)));

    public Step Mirror() => _ => _
        .Requires(() => Fallback ?? NuGetApiKey)
        .Executes(() => Executed.Add(nameof(Mirror)));

    public Step SignPackages() => _ => _
        .Requires(() => Sign)
        .Executes(() => Executed.Add(nameof(SignPackages)));

    public Step Upload() => _ => _
        .Requires(() => Feeds.Length > 0)
        .Executes(() => Executed.Add(nameof(Upload)));

    public Step Deploy() => _ => _
        .Requires(() => Approved)
        .Executes(() => Executed.Add(nameof(Deploy)));
}
