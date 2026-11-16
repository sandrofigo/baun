using System.Text.RegularExpressions;
using Baun.CI;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Baun.Tests;

public class BuildRunnerTests
{
    [Fact]
    public async Task Runs_the_default_step_and_its_dependencies_in_order()
    {
        var build = new SampleBuild();

        var result = await RunAsync(build);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Clean", "Restore", "Compile", "Test"], build.Executed);
        Assert.Matches(@"Compile\s+Succeeded", result.Output);
        Assert.Contains("Build succeeded", result.Output);
    }

    [Fact]
    public async Task Runs_only_the_steps_the_requested_step_needs()
    {
        var build = new SampleBuild();

        var result = await RunAsync(build, "Compile");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Clean", "Restore", "Compile"], build.Executed);
    }

    [Theory]
    [InlineData("clean")]
    [InlineData("CLEAN")]
    [InlineData("cLeAn")]
    public async Task Matches_step_names_case_insensitively(string name)
    {
        var build = new SampleBuild();

        var result = await RunAsync(build, name);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Clean"], build.Executed);
    }

    [Fact]
    public async Task Runs_a_shared_dependency_only_once()
    {
        var build = new SampleBuild();

        var result = await RunAsync(build, "Test", "Pack");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Clean", "Restore", "Compile", "Test", "Pack"], build.Executed);
    }

    [Fact]
    public async Task Awaits_asynchronous_steps()
    {
        var build = new SampleBuild();

        var result = await RunAsync(build, "Pack");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Pack", build.Executed[^1]);
        Assert.True(build.PackCompleted);
    }

    [Fact]
    public async Task Runs_steps_without_work()
    {
        var build = new SampleBuild();

        var result = await RunAsync(build, "All");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Clean", "Restore", "Compile", "Test", "Pack"], build.Executed);
        Assert.Matches(@"All\s+Succeeded", result.Output);
    }

    [Fact]
    public async Task Fails_for_an_unknown_step_and_lists_the_available_steps()
    {
        var build = new SampleBuild();

        var result = await RunAsync(build, "Deploy");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Contains("Unknown step Deploy. Available steps: Clean, Restore, Compile, Test, Pack, All.", result.Output);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("Compile", "--HELP")]
    public async Task Shows_help_with_the_steps_instead_of_running_them(params string[] arguments)
    {
        var build = new SampleBuild();

        var result = await RunAsync(build, arguments);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Contains("USAGE:", result.Output);
        Assert.Matches(@"\n\s+Clean\s*\n", result.Output);
        Assert.Matches(@"\n\s+Test\s+Runs when no step is named", result.Output);
        Assert.Contains("--step <name>", result.Output);
    }

    [Fact]
    public async Task Colors_only_the_headings_and_usage_placeholders_of_the_help()
    {
        var console = new TestConsole().Width(250).EmitAnsiSequences().Colors(ColorSystem.TrueColor);

        var exitCode = await new BuildRunner(new SampleBuild(), console).RunAsync(["--help"]);

        Assert.Equal(0, exitCode);
        var colored = Regex.Matches(console.Output, @"\e\[38;[0-9;]+m([^\e]*)\e\[0m").Select(match => match.Groups[1].Value);
        Assert.Equal(["USAGE:", "[steps] [OPTIONS]", "STEPS:", "OPTIONS:"], colored);
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    public async Task Shows_the_version_instead_of_running_steps(string argument)
    {
        var build = new SampleBuild();

        var result = await RunAsync(build, argument);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Matches(@"^\d+\.\d+\.\d+\S*$", result.Output.Trim());
    }

    [Fact]
    public async Task Fails_when_no_step_is_named_and_there_is_no_entry_step()
    {
        var build = new NoEntryBuild();

        var result = await RunAsync(build);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("No step was named and NoEntryBuild has no entry step. Name a step, or add [Entry(nameof(...))] to the build class. Available steps: Compile.", result.Output);
    }

    [Fact]
    public async Task Runs_a_named_step_when_there_is_no_entry_step()
    {
        var build = new NoEntryBuild();

        var result = await RunAsync(build, "Compile");

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Fails_for_an_unknown_option()
    {
        var build = new SampleBuild();

        var result = await RunAsync(build, "--verbose");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown option --verbose.", result.Output);
    }

    [Fact]
    public async Task Runs_a_step_named_with_the_step_option()
    {
        var build = new SampleBuild();

        var result = await RunAsync(build, "--step", "Compile");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Clean", "Restore", "Compile"], build.Executed);
    }

    [Fact]
    public async Task Mixes_the_step_option_with_step_names()
    {
        var build = new SampleBuild();

        var result = await RunAsync(build, "Clean", "--STEP", "compile", "Restore");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Clean", "Restore", "Compile"], build.Executed);
    }

    [Theory]
    [InlineData("--step")]
    [InlineData("--step", "--step", "Compile")]
    public async Task Fails_for_the_step_option_without_a_step_name(params string[] arguments)
    {
        var build = new SampleBuild();

        var result = await RunAsync(build, arguments);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Contains("Option --step requires a step name.", result.Output);
    }

    [Fact]
    public async Task Stops_at_the_first_failing_step()
    {
        var build = new FailingBuild();

        var result = await RunAsync(build);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["First"], build.Executed);
        Assert.Matches(@"Failing\s+Failed", result.Output);
        Assert.Contains("Boom", result.Output);
        Assert.Matches(@"Last\s+Skipped", result.Output);
        Assert.Contains("Build failed", result.Output);
    }

    [Fact]
    public async Task Fails_for_a_circular_dependency()
    {
        var build = new CircularBuild();

        var result = await RunAsync(build);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Circular dependency: A -> B -> C -> A.", result.Output);
    }

    [Fact]
    public async Task Fails_for_a_dependency_that_is_not_a_step()
    {
        var build = new ForeignDependencyBuild();

        var result = await RunAsync(build);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Step Main depends on Helper, which is not a step of ForeignDependencyBuild.", result.Output);
    }

    [Fact]
    public async Task Writes_an_empty_line_before_and_after_each_step_name()
    {
        var build = new FailingBuild();

        var result = await RunAsync(build, "First");

        string[] lines = result.Output.ReplaceLineEndings("\n").Split('\n');
        int header = Array.IndexOf(lines, "▸ First");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, header);
        Assert.Equal(string.Empty, lines[header - 1]);
        Assert.Equal(string.Empty, lines[header + 1]);
    }

    [Fact]
    public async Task Writes_each_step_in_a_log_group_in_CI()
    {
        var build = new SampleBuild();

        var result = await RunInCIAsync(build, CIProvider.GitHubActions, "Compile");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("▸", result.Output);

        string[] lines = result.Output.ReplaceLineEndings("\n").Split('\n');
        Assert.Equal(
            ["::group::Clean", "::endgroup::", "::group::Restore", "::endgroup::", "::group::Compile", "::endgroup::"],
            lines.Where(line => line.StartsWith("::", StringComparison.Ordinal)));

        int lastGroupEnd = Array.LastIndexOf(lines, "::endgroup::");
        Assert.Contains(lines.Skip(lastGroupEnd), line => line.Contains("Build succeeded"));
        Assert.DoesNotContain(lines.Take(lastGroupEnd), line => line.Contains("Succeeded"));
    }

    [Fact]
    public async Task Writes_the_error_of_a_failing_step_after_its_log_group()
    {
        var build = new FailingBuild();

        var result = await RunInCIAsync(build, CIProvider.GitHubActions);

        Assert.Equal(1, result.ExitCode);

        string output = result.Output;
        int failingGroupEnd = output.IndexOf("::endgroup::", output.IndexOf("::group::Failing", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(failingGroupEnd >= 0);
        Assert.True(output.IndexOf("Boom", StringComparison.Ordinal) > failingGroupEnd);
        Assert.DoesNotContain("::group::Last", output);
    }

    [Fact]
    public async Task Writes_a_group_inside_a_step_as_a_line_in_CI()
    {
        var console = new TestConsole().Width(250);
        var build = new GroupingBuild(console);

        var exitCode = await new BuildRunner(build, console, ParameterSources.None, CIProvider.GitHubActions).RunAsync([]);
        var result = new RunResult(exitCode, console.Output);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, CountOccurrences(result.Output, "::group::"));
        Assert.Equal(1, CountOccurrences(result.Output, "::endgroup::"));
        Assert.Contains("› Inner", result.Output);
    }

    private static int CountOccurrences(string text, string value) =>
        (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;

    private static async Task<RunResult> RunInCIAsync(IBaunBuild build, CIProvider provider, params string[] arguments)
    {
        var console = new TestConsole().Width(250);

        var exitCode = await new BuildRunner(build, console, ParameterSources.None, provider).RunAsync(arguments);

        return new RunResult(exitCode, console.Output);
    }

    private static async Task<RunResult> RunAsync(IBaunBuild build, params string[] arguments)
    {
        var console = new TestConsole().Width(250);

        var exitCode = await new BuildRunner(build, console, ParameterSources.None, CIProvider.None).RunAsync(arguments);

        return new RunResult(exitCode, console.Output);
    }

    private sealed record RunResult(int ExitCode, string Output);
}

[Entry(nameof(Test))]
internal sealed partial class SampleBuild
{
    public List<string> Executed { get; } = [];

    public bool PackCompleted { get; private set; }

    public Step Clean() => _ => _
        .Executes(() => Executed.Add(nameof(Clean)));

    public Step Restore() => _ => _
        .Executes(() => Executed.Add(nameof(Restore)));

    public Step Compile() => _ => _
        .DependsOn(Clean, Restore)
        .Executes(() => Executed.Add(nameof(Compile)));

    public Step Test() => _ => _
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await Task.Yield();
            Executed.Add(nameof(Test));
        });

    public Step Pack() => _ => _
        .DependsOn(Test, Compile)
        .Executes(async () =>
        {
            Executed.Add(nameof(Pack));
            await Task.Delay(10);
            PackCompleted = true;
        });

    public Step All() => _ => _
        .DependsOn(Pack);
}

[Entry(nameof(Last))]
internal sealed partial class FailingBuild
{
    public List<string> Executed { get; } = [];

    public Step First() => _ => _
        .Executes(() => Executed.Add(nameof(First)));

    public Step Failing() => _ => _
        .DependsOn(First)
        .Executes(() => throw new InvalidOperationException("Boom"));

    public Step Last() => _ => _
        .DependsOn(Failing)
        .Executes(() => Executed.Add(nameof(Last)));
}

[Entry(nameof(Main))]
internal sealed partial class GroupingBuild(IAnsiConsole console)
{
    public Step Main() => _ => _
        .Executes(async () =>
        {
            await Task.Yield();

            using (LogGroups.Begin(console, CIProvider.GitHubActions, "Inner", "› Inner"))
            {
                console.WriteLine("inside");
            }
        });
}

[Entry(nameof(A))]
internal sealed partial class CircularBuild
{
    public Step A() => _ => _.DependsOn(B);

    public Step B() => _ => _.DependsOn(C);

    public Step C() => _ => _.DependsOn(A);
}

[Entry(nameof(Main))]
internal sealed partial class ForeignDependencyBuild
{
    public Step Main() => _ => _.DependsOn(Helper);

    private static Step Helper() => _ => _;
}

internal sealed partial class NoEntryBuild
{
    public Step Compile() => _ => _;
}
