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
        Assert.Contains("Running Clean -> Restore -> Compile -> Test", result.Output);
        Assert.Matches(@"Compile\s+│\s+Succeeded", result.Output);
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
        Assert.Contains("Step All succeeded", result.Output);
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
    public async Task Stops_at_the_first_failing_step()
    {
        var build = new FailingBuild();

        var result = await RunAsync(build);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["First"], build.Executed);
        Assert.Contains("Step Failing failed", result.Output);
        Assert.Contains("Boom", result.Output);
        Assert.Matches(@"Last\s+│\s+Skipped", result.Output);
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

    private static async Task<RunResult> RunAsync(IBaunBuild build, params string[] arguments)
    {
        var console = new TestConsole().Width(250);

        var exitCode = await new BuildRunner(build, console).RunAsync(arguments);

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
