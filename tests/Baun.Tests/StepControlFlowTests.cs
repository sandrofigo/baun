using Baun.CI;
using Baun.Plan;
using Spectre.Console.Testing;

namespace Baun.Tests;

public class StepControlFlowTests
{
    [Fact]
    public async Task Skips_a_step_whose_condition_is_false_and_runs_its_dependents()
    {
        var build = new ConditionBuild { ComputeEnables = true };

        var result = await RunAsync(build);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Compute", "Report"], build.Executed);
        Assert.Equal(["Compute", "Report"], build.Started);
        Assert.Contains("Skipping Publish because PublishEnabled is false", result.Output);
        Assert.Matches(@"Publish\s+Skipped", result.Output);
        Assert.Contains("Build succeeded", result.Output);
    }

    [Fact]
    public async Task Runs_a_step_whose_conditions_are_all_true()
    {
        var build = new ConditionBuild { PublishEnabled = true, ComputeEnables = true };

        var result = await RunAsync(build);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Compute", "Publish", "Report"], build.Executed);
    }

    [Fact]
    public async Task Checks_a_condition_after_the_dependencies_ran()
    {
        var build = new ConditionBuild { PublishEnabled = true, ComputeEnables = false };

        var result = await RunAsync(build);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Compute", "Report"], build.Executed);
        Assert.Contains("Skipping Publish because computed is false", result.Output);
    }

    [Fact]
    public async Task Fails_a_step_whose_condition_throws()
    {
        var build = new ConditionBuild();

        var result = await RunAsync(build, "Throwing");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Contains("Bad condition", result.Output);
        Assert.Matches(@"Throwing\s+Failed", result.Output);
    }

    [Fact]
    public async Task Checks_the_requirements_of_a_step_with_a_condition_only_when_it_runs()
    {
        var skipped = new ConditionBuild();
        var skippedResult = await RunAsync(skipped, "Deploy");

        Assert.Equal(0, skippedResult.ExitCode);
        Assert.Matches(@"Deploy\s+Skipped", skippedResult.Output);

        var running = new ConditionBuild { PublishEnabled = true };
        var runningResult = await RunAsync(running, "Deploy");

        Assert.Equal(1, runningResult.ExitCode);
        Assert.Empty(running.Executed);
        Assert.Contains("Step Deploy requires parameter ApiKey.", runningResult.Output);
        Assert.Matches(@"Deploy\s+Failed", runningResult.Output);
    }

    [Fact]
    public async Task Lists_conditions_and_triggers_in_the_help()
    {
        var conditions = await RunAsync(new ConditionBuild(), "--help");
        var triggers = await RunAsync(new TriggerBuild(), "--help");

        Assert.Contains("Only when PublishEnabled and computed", conditions.Output);
        Assert.Contains("Triggers Notify, Cleanup", triggers.Output);
    }

    [Fact]
    public async Task Doesnt_run_a_step_that_only_runs_after_another()
    {
        var build = new OrderBuild();

        var result = await RunAsync(build, "Restore");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Restore"], build.Executed);
    }

    [Fact]
    public async Task Runs_a_step_after_the_steps_it_runs_after()
    {
        var build = new OrderBuild();

        var result = await RunAsync(build, "Restore", "Clean");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Clean", "Restore"], build.Executed);
    }

    [Fact]
    public async Task Runs_a_step_before_the_steps_it_runs_before()
    {
        var build = new OrderBuild();

        var result = await RunAsync(build, "Compile", "Lint");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Restore", "Lint", "Compile"], build.Executed);
    }

    [Fact]
    public async Task Fails_for_a_circular_order()
    {
        var result = await RunAsync(new OrderCycleBuild(), "A", "B");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Circular order: A -> B -> A.", result.Output);
    }

    [Fact]
    public async Task Fails_for_an_order_with_a_method_that_is_not_a_step()
    {
        var result = await RunAsync(new ForeignOrderBuild());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Step Main runs after Helper, which is not a step of ForeignOrderBuild.", result.Output);
    }

    [Fact]
    public async Task Runs_triggered_steps_and_their_dependencies_after_the_trigger()
    {
        var build = new TriggerBuild();

        var result = await RunAsync(build, "Publish", "Test");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Compile", "Publish", "Prepare", "Notify", "Cleanup", "Test"], build.Executed);
    }

    [Fact]
    public async Task Doesnt_run_the_trigger_of_a_triggered_step()
    {
        var build = new TriggerBuild();

        var result = await RunAsync(build, "Notify");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Prepare", "Notify"], build.Executed);
    }

    [Fact]
    public async Task Proceeds_after_a_failing_step_but_skips_its_dependents()
    {
        var build = new ProceedBuild();

        var result = await RunAsync(build);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["Other"], build.Executed);
        Assert.Matches(@"Flaky\s+Failed", result.Output);
        Assert.Matches(@"Dependent\s+Skipped", result.Output);
        Assert.Matches(@"Other\s+Succeeded", result.Output);
        Assert.Matches(@"All\s+Skipped", result.Output);
        Assert.Contains("Build failed", result.Output);
    }

    [Fact]
    public async Task Runs_a_step_that_always_runs_after_a_failure()
    {
        var build = new AlwaysBuild();

        var result = await RunAsync(build, "Last", "Cleanup");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["First", "Cleanup"], build.Executed);
        Assert.Matches(@"Last\s+Skipped", result.Output);
        Assert.Matches(@"Cleanup\s+Succeeded", result.Output);
    }

    [Theory]
    [InlineData("--skip", "Test")]
    [InlineData("--SKIP", "test")]
    [InlineData("--baun-skip", "Test")]
    public async Task Skips_a_step_named_with_skip(string option, string step)
    {
        var build = new SampleBuild();

        var result = await RunAsync(build, "Pack", option, step);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Clean", "Restore", "Compile", "Pack"], build.Executed);
        Assert.Matches(@"Test\s+Skipped", result.Output);
    }

    [Fact]
    public async Task Skips_several_steps()
    {
        var build = new SampleBuild();

        var result = await RunAsync(build, "Compile", "--skip", "Clean", "--skip", "Restore");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Compile"], build.Executed);
    }

    [Theory]
    [InlineData("Pack", "--skip")]
    [InlineData("--skip", "--step", "Pack")]
    public async Task Skips_all_steps_but_the_named_ones_with_skip_alone(params string[] arguments)
    {
        var build = new SampleBuild();

        var result = await RunAsync(build, arguments);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Pack"], build.Executed);
    }

    [Fact]
    public async Task Fails_for_an_unknown_step_to_skip()
    {
        var build = new SampleBuild();

        var result = await RunAsync(build, "--skip", "Deploy");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(build.Executed);
        Assert.Contains("Unknown step Deploy.", result.Output);
    }

    [Fact]
    public async Task Doesnt_check_the_requirements_of_a_skipped_step()
    {
        var build = new SkipRequirementBuild();

        var result = await RunAsync(build, "Release", "--skip", "Publish");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["Release"], build.Executed);
    }

    [Fact]
    public async Task Lists_skip_in_the_help()
    {
        var result = await RunAsync(new SampleBuild(), "--help");

        Assert.Contains("--skip [<step>]", result.Output);
    }

    [Fact]
    public async Task Shows_triggers_skipped_steps_and_conditions_in_the_plan()
    {
        string triggers = await PlanAsync(new TriggerBuild(), "--plan", "Publish", "--skip", "Compile");
        string conditions = await PlanAsync(new ConditionBuild(), "--plan");

        Assert.Contains("""{"name":"Compile","dependencies":[],"order":1,"skipped":true}""", triggers);
        Assert.Contains("""{"name":"Publish","dependencies":["Compile"],"order":2,"triggers":["Notify","Cleanup"]}""", triggers);
        string publish = conditions[conditions.IndexOf("""{"name":"Publish",""", StringComparison.Ordinal)..];
        publish = publish[..(publish.IndexOf('}', StringComparison.Ordinal) + 1)];
        Assert.Equal("""{"name":"Publish","dependencies":["Compute"],"order":2,"condition":"PublishEnabled \u0026\u0026 computed"}""", publish);
    }

    private static async Task<RunResult> RunAsync(IBaunBuild build, params string[] arguments)
    {
        var console = new TestConsole().Width(250);

        var exitCode = await new BuildRunner(build, console, ParameterSources.None, CIProvider.None, openInBrowser: _ => false).RunAsync(arguments);

        return new RunResult(exitCode, console.Output);
    }

    /// <summary>
    /// Runs the build with a temporary folder for the plan page and returns the page.
    /// </summary>
    private static async Task<string> PlanAsync(IBaunBuild build, params string[] arguments)
    {
        var folder = (AbsolutePath)Directory.CreateTempSubdirectory("baun-plan-tests-").FullName;

        try
        {
            var exitCode = await new BuildRunner(build, new TestConsole(), ParameterSources.None, CIProvider.None, _ => true, folder)
                .RunAsync(arguments);

            Assert.Equal(0, exitCode);
            return File.ReadAllText(folder / PlanPage.FileName);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private sealed record RunResult(int ExitCode, string Output);
}

[Entry(nameof(Report))]
internal sealed partial class ConditionBuild : IOnStepStarted
{
    private bool computed;

    public List<string> Executed { get; } = [];

    public List<string> Started { get; } = [];

    public bool PublishEnabled { get; init; }

    public bool ComputeEnables { get; init; }

    [Parameter]
    public string? ApiKey { get; set; }

    public Step Compute() => _ => _
        .Executes(() =>
        {
            Executed.Add(nameof(Compute));
            computed = ComputeEnables;
        });

    public Step Publish() => _ => _
        .DependsOn(Compute)
        .OnlyWhen(() => PublishEnabled)
        .OnlyWhen(() => computed)
        .Executes(() => Executed.Add(nameof(Publish)));

    public Step Report() => _ => _
        .DependsOn(Publish)
        .Executes(() => Executed.Add(nameof(Report)));

    public Step Throwing() => _ => _
        .OnlyWhen(() => throw new InvalidOperationException("Bad condition"))
        .Executes(() => Executed.Add(nameof(Throwing)));

    public Step Deploy() => _ => _
        .OnlyWhen(() => PublishEnabled)
        .Requires(() => ApiKey)
        .Executes(() => Executed.Add(nameof(Deploy)));

    public Task OnStepStartedAsync(string step)
    {
        Started.Add(step);
        return Task.CompletedTask;
    }
}

internal sealed partial class OrderBuild
{
    public List<string> Executed { get; } = [];

    public Step Clean() => _ => _
        .Executes(() => Executed.Add(nameof(Clean)));

    public Step Restore() => _ => _
        .After(Clean)
        .Executes(() => Executed.Add(nameof(Restore)));

    public Step Compile() => _ => _
        .DependsOn(Restore)
        .Executes(() => Executed.Add(nameof(Compile)));

    public Step Lint() => _ => _
        .Before(Compile)
        .Executes(() => Executed.Add(nameof(Lint)));
}

internal sealed partial class OrderCycleBuild
{
    public Step A() => _ => _.After(B);

    public Step B() => _ => _.DependsOn(A);
}

[Entry(nameof(Main))]
internal sealed partial class ForeignOrderBuild
{
    public Step Main() => _ => _.After(Helper);

    private static Step Helper() => _ => _;
}

internal sealed partial class TriggerBuild
{
    public List<string> Executed { get; } = [];

    public Step Compile() => _ => _
        .Executes(() => Executed.Add(nameof(Compile)));

    public Step Publish() => _ => _
        .DependsOn(Compile)
        .Triggers(Notify)
        .Executes(() => Executed.Add(nameof(Publish)));

    public Step Prepare() => _ => _
        .Executes(() => Executed.Add(nameof(Prepare)));

    public Step Notify() => _ => _
        .DependsOn(Prepare)
        .Executes(() => Executed.Add(nameof(Notify)));

    public Step Cleanup() => _ => _
        .TriggeredBy(Publish)
        .Executes(() => Executed.Add(nameof(Cleanup)));

    public Step Test() => _ => _
        .DependsOn(Compile)
        .Executes(() => Executed.Add(nameof(Test)));
}

[Entry(nameof(All))]
internal sealed partial class ProceedBuild
{
    public List<string> Executed { get; } = [];

    public Step Flaky() => _ => _
        .ProceedAfterFailure()
        .Executes(() => throw new InvalidOperationException("Flaky"));

    public Step Dependent() => _ => _
        .DependsOn(Flaky)
        .Executes(() => Executed.Add(nameof(Dependent)));

    public Step Other() => _ => _
        .Executes(() => Executed.Add(nameof(Other)));

    public Step All() => _ => _
        .DependsOn(Dependent, Other)
        .Executes(() => Executed.Add(nameof(All)));
}

internal sealed partial class AlwaysBuild
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

    public Step Cleanup() => _ => _
        .After(Failing)
        .AlwaysRuns()
        .Executes(() => Executed.Add(nameof(Cleanup)));
}

internal sealed partial class SkipRequirementBuild
{
    public List<string> Executed { get; } = [];

    [Parameter]
    public string? ApiKey { get; set; }

    public Step Publish() => _ => _
        .Requires(() => ApiKey)
        .Executes(() => Executed.Add(nameof(Publish)));

    public Step Release() => _ => _
        .DependsOn(Publish)
        .Executes(() => Executed.Add(nameof(Release)));
}
