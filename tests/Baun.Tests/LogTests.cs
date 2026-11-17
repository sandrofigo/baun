using System.Text.RegularExpressions;
using Baun.CI;
using Spectre.Console.Testing;

namespace Baun.Tests;

public class LogTests
{
    [Fact]
    public async Task Writes_a_message_with_the_time_and_level()
    {
        var result = await RunAsync(new LoggingBuild(), "Values");

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(new Regex(@"^\d\d:\d\d:\d\d INF Built Baun in Release$", RegexOptions.Multiline), result.Output);
    }

    [Fact]
    public async Task Writes_property_values_in_the_invariant_culture_and_strings_without_quotes()
    {
        var result = await RunAsync(new LoggingBuild(), "Values");

        Assert.Contains("INF 1.5 and 42 and [1, 2]", result.Output);
        Assert.Contains("INF {Literal} and {Missing}", result.Output);
    }

    [Fact]
    public async Task Writes_messages_of_information_and_above_by_default()
    {
        var result = await RunAsync(new LoggingBuild(), "Levels");

        Assert.DoesNotContain("VRB", result.Output);
        Assert.DoesNotContain("DBG", result.Output);
        Assert.Contains("INF information", result.Output);
        Assert.Contains("WRN warning", result.Output);
        Assert.Contains("ERR error", result.Output);
        Assert.Contains("FTL fatal", result.Output);
    }

    [Theory]
    [InlineData("--verbosity", "debug")]
    [InlineData("--verbosity=Debug")]
    [InlineData("--VERBOSITY", "DEBUG")]
    public async Task Writes_debug_messages_with_verbosity_debug(params string[] options)
    {
        var result = await RunAsync(new LoggingBuild(), ["Levels", .. options]);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("VRB", result.Output);
        Assert.Contains("DBG debug", result.Output);
        Assert.Contains("INF information", result.Output);
    }

    [Fact]
    public async Task Reads_the_verbosity_from_the_environment()
    {
        var result = await RunWithEnvironmentAsync(new LoggingBuild(), new() { [BuildRunner.VerbosityVariable] = "verbose" }, "Levels");

        Assert.Contains("VRB verbose", result.Output);
        Assert.Contains("DBG debug", result.Output);
    }

    [Fact]
    public async Task Prefers_the_verbosity_option_to_the_environment()
    {
        var result = await RunWithEnvironmentAsync(new LoggingBuild(), new() { [BuildRunner.VerbosityVariable] = "verbose" }, "Levels", "--verbosity", "warning");

        Assert.DoesNotContain("INF information", result.Output);
        Assert.Contains("WRN warning", result.Output);
    }

    [Theory]
    [InlineData("loud")]
    [InlineData("3")]
    public async Task Fails_for_an_unknown_verbosity(string value)
    {
        var build = new LoggingBuild();

        var result = await RunAsync(build, "Levels", "--verbosity", value);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Matches(new Regex($@"^\d\d:\d\d:\d\d ERR Unknown verbosity {value}\.", RegexOptions.Multiline), result.Output);
    }

    [Fact]
    public async Task Fails_for_a_verbosity_option_without_a_level()
    {
        var result = await RunAsync(new LoggingBuild(), "Levels", "--verbosity");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ERR Option --verbosity requires a level", result.Output);
    }

    [Fact]
    public async Task Fails_for_a_parameter_named_verbosity()
    {
        var result = await RunAsync(new VerbosityParameterBuild(), "Compile");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ERR Parameter Verbosity can't be used, because --verbosity is an option of baun.", result.Output);
    }

    [Fact]
    public async Task Lists_the_verbosity_option_in_the_help()
    {
        var result = await RunAsync(new LoggingBuild(), "--help");

        Assert.Contains("--verbosity <level>", result.Output);
        Assert.Contains(BuildRunner.VerbosityVariable, result.Output);
    }

    [Fact]
    public async Task Writes_the_exception_after_the_message()
    {
        var result = await RunAsync(new LoggingBuild(), "Exception");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("WRN Retrying", result.Output);
        Assert.Contains("Flaky", result.Output);
    }

    [Fact]
    public async Task Indents_further_lines_of_a_message()
    {
        var result = await RunAsync(new LoggingBuild(), "MultiLine");

        Assert.Contains($"INF first{Environment.NewLine}             second", result.Output.ReplaceLineEndings());
    }

    [Fact]
    public async Task Logs_the_exception_of_a_failing_step()
    {
        var result = await RunAsync(new FailingBuild());

        Assert.Equal(1, result.ExitCode);
        Assert.Matches(new Regex(@"^\d\d:\d\d:\d\d ERR Step Failing failed$", RegexOptions.Multiline), result.Output);
        Assert.Contains("Boom", result.Output);
    }

    [Fact]
    public async Task Logs_each_configuration_error_line_as_its_own_error()
    {
        var result = await RunAsync(new TwoRequirementsBuild(), "Deploy");

        Assert.Equal(1, result.ExitCode);
        Assert.Matches(new Regex(@"^\d\d:\d\d:\d\d ERR Step Deploy requires parameter ApiKey\.", RegexOptions.Multiline), result.Output);
        Assert.Matches(new Regex(@"^\d\d:\d\d:\d\d ERR Step Deploy requires parameter Server\.", RegexOptions.Multiline), result.Output);
    }

    [Fact]
    public async Task Logs_configuration_errors_with_braces_as_they_are()
    {
        var result = await RunAsync(new LoggingBuild(), "{Deploy}");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ERR Unknown step {Deploy}.", result.Output);
    }

    [Fact]
    public void Writes_outside_of_a_build_without_failing()
    {
        Log.Information("Outside {Build}", "nothing");

        Assert.True(Log.IsEnabled(LogLevel.Information));
        Assert.False(Log.IsEnabled(LogLevel.Debug));
    }

    private static Task<RunResult> RunAsync(IBaunBuild build, params string[] arguments) =>
        RunWithEnvironmentAsync(build, [], arguments);

    private static async Task<RunResult> RunWithEnvironmentAsync(IBaunBuild build, Dictionary<string, string> environment, params string[] arguments)
    {
        var console = new TestConsole().Width(250);

        var exitCode = await new BuildRunner(build, console, new ParameterSources(environment, null), CIProvider.None).RunAsync(arguments);

        return new RunResult(exitCode, console.Output);
    }

    private sealed record RunResult(int ExitCode, string Output);
}

[Entry(nameof(Levels))]
internal sealed partial class LoggingBuild
{
    public List<string> Executed { get; } = [];

    public Step Levels() => _ => _
        .Executes(() =>
        {
            Executed.Add(nameof(Levels));
            Log.Verbose("verbose");
            Log.Debug("debug");
            Log.Information("information");
            Log.Warning("warning");
            Log.Error("error");
            Log.Fatal("fatal");
        });

    public Step Values() => _ => _
        .Executes(() =>
        {
            Log.Information("Built {Project} in {Configuration}", "Baun", "Release");
            Log.Information("{Number} and {Count} and {Items}", 1.5, 42, new[] { 1, 2 });
            Log.Information("{{Literal}} and {Missing}");
        });

    public Step Exception() => _ => _
        .Executes(() => Log.Warning(new InvalidOperationException("Flaky"), "Retrying {Step}", "Exception"));

    public Step MultiLine() => _ => _
        .Executes(() => Log.Information("first\nsecond"));
}

internal sealed partial class VerbosityParameterBuild
{
    [Parameter]
    public string? Verbosity { get; set; }

    public Step Compile() => _ => _;
}

internal sealed partial class TwoRequirementsBuild
{
    [Parameter]
    public string? ApiKey { get; set; }

    [Parameter]
    public string? Server { get; set; }

    public Step Deploy() => _ => _
        .Requires(() => ApiKey)
        .Requires(() => Server);
}
