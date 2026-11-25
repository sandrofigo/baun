using Baun.CI;
using Spectre.Console.Testing;

namespace Baun.Tests;

public class HookTests
{
    [Fact]
    public async Task Runs_the_hooks_of_a_successful_build_in_order()
    {
        var build = new HookBuild();

        var result = await RunAsync(build);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
        [
            "BuildCreated",
            "BuildInitialized",
            "StepStarted First",
            "Execute First",
            "StepSucceeded First",
            "StepCompleted First",
            "StepStarted Last",
            "Execute Last",
            "StepSucceeded Last",
            "StepCompleted Last",
            "BuildSucceeded",
            "BuildCompleted"
        ], build.Calls);
    }

    [Fact]
    public async Task Runs_the_failure_hooks_when_a_step_fails()
    {
        var build = new HookBuild { FailingStep = "First" };

        var result = await RunAsync(build);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(
        [
            "BuildCreated",
            "BuildInitialized",
            "StepStarted First",
            "Execute First",
            "StepFailed First Boom",
            "StepCompleted First",
            "BuildFailed",
            "BuildCompleted"
        ], build.Calls);
        Assert.Contains("Build failed", result.Output);
    }

    [Fact]
    public async Task Runs_only_the_created_hook_for_help()
    {
        var build = new HookBuild();

        var result = await RunAsync(build, "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["BuildCreated"], build.Calls);
    }

    [Fact]
    public async Task Runs_only_the_created_hook_for_the_plan()
    {
        var build = new HookBuild();
        var folder = (AbsolutePath)Directory.CreateTempSubdirectory("baun-hook-tests-").FullName;

        try
        {
            var exitCode = await new BuildRunner(build, new TestConsole(), ParameterSources.None, CIProviderType.None, _ => true, folder)
                .RunAsync(["--plan"]);

            Assert.Equal(0, exitCode);
            Assert.Equal(["BuildCreated"], build.Calls);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Runs_only_the_created_hook_when_the_build_cant_start()
    {
        var build = new HookBuild();

        var result = await RunAsync(build, "Unknown");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["BuildCreated"], build.Calls);
    }

    [Fact]
    public async Task Fails_without_running_steps_when_the_created_hook_throws()
    {
        var build = new HookBuild { FailingHook = "BuildCreated" };

        var result = await RunAsync(build);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["BuildCreated"], build.Calls);
        Assert.Contains("Hook OnBuildCreatedAsync failed", result.Output);
    }

    [Fact]
    public async Task Fails_without_running_steps_when_the_initialized_hook_throws()
    {
        var build = new HookBuild { FailingHook = "BuildInitialized" };

        var result = await RunAsync(build);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["BuildCreated", "BuildInitialized", "BuildFailed", "BuildCompleted"], build.Calls);
        Assert.Contains("Hook OnBuildInitializedAsync failed", result.Output);
        Assert.Matches(@"First\s+Skipped", result.Output);
    }

    [Fact]
    public async Task Fails_the_step_when_the_started_hook_throws()
    {
        var build = new HookBuild { FailingHook = "StepStarted" };

        var result = await RunAsync(build);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(
        [
            "BuildCreated",
            "BuildInitialized",
            "StepStarted First",
            "StepFailed First Hook StepStarted",
            "StepCompleted First",
            "BuildFailed",
            "BuildCompleted"
        ], build.Calls);
        Assert.Matches(@"First\s+Failed", result.Output);
    }

    [Fact]
    public async Task Fails_the_step_when_the_succeeded_hook_throws()
    {
        var build = new HookBuild { FailingHook = "StepSucceeded" };

        var result = await RunAsync(build);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("StepFailed First Hook StepSucceeded", build.Calls);
        Assert.Matches(@"First\s+Failed", result.Output);
    }

    [Fact]
    public async Task Fails_the_build_when_the_succeeded_hook_throws()
    {
        var build = new HookBuild { FailingHook = "BuildSucceeded" };

        var result = await RunAsync(build);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["BuildSucceeded", "BuildCompleted"], build.Calls[^2..]);
        Assert.DoesNotContain("BuildFailed", build.Calls);
        Assert.Contains("Hook OnBuildSucceededAsync failed", result.Output);
        Assert.Contains("Build failed", result.Output);
    }

    [Fact]
    public async Task Fails_the_build_when_the_completed_hook_throws()
    {
        var build = new HookBuild { FailingHook = "BuildCompleted" };

        var result = await RunAsync(build);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Hook OnBuildCompletedAsync failed", result.Output);
        Assert.Contains("Build failed", result.Output);
    }

    [Fact]
    public async Task Skips_the_remaining_steps_when_the_step_completed_hook_throws()
    {
        var build = new HookBuild { FailingHook = "StepCompleted" };

        var result = await RunAsync(build);

        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain("Execute Last", build.Calls);
        Assert.Matches(@"First\s+Succeeded", result.Output);
        Assert.Matches(@"Last\s+Skipped", result.Output);
    }

    [Fact]
    public async Task Runs_only_the_hooks_a_build_implements()
    {
        var build = new BuildFailedHookBuild();

        var result = await RunAsync(build);

        Assert.Equal(1, result.ExitCode);
        Assert.True(build.Failed);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(IBaunBuild build, params string[] arguments)
    {
        var console = new TestConsole().Width(250);

        var exitCode = await new BuildRunner(build, console, ParameterSources.None, CIProviderType.None, openInBrowser: _ => false).RunAsync(arguments);

        return (exitCode, console.Output);
    }
}

[Entry(nameof(Last))]
internal sealed partial class HookBuild :
    IOnBuildCreated,
    IOnBuildInitialized,
    IOnStepStarted,
    IOnStepSucceeded,
    IOnStepFailed,
    IOnStepCompleted,
    IOnBuildSucceeded,
    IOnBuildFailed,
    IOnBuildCompleted
{
    public List<string> Calls { get; } = [];

    /// <summary>
    /// The step that throws, if any.
    /// </summary>
    public string? FailingStep { get; init; }

    /// <summary>
    /// The hook that throws, such as <c>StepStarted</c>, if any.
    /// </summary>
    public string? FailingHook { get; init; }

    public Step First() => _ => _
        .Executes(() => Execute(nameof(First)));

    public Step Last() => _ => _
        .DependsOn(First)
        .Executes(async () =>
        {
            await Task.Yield();
            Execute(nameof(Last));
        });

    private void Execute(string step)
    {
        Calls.Add($"Execute {step}");

        if (step == FailingStep)
        {
            throw new InvalidOperationException("Boom");
        }
    }

    public Task OnBuildCreatedAsync() => Record("BuildCreated");

    public Task OnBuildInitializedAsync() => Record("BuildInitialized");

    public Task OnStepStartedAsync(string step) => Record("StepStarted", step);

    public Task OnStepSucceededAsync(string step) => Record("StepSucceeded", step);

    public Task OnStepFailedAsync(string step, Exception exception) => Record("StepFailed", $"{step} {exception.Message}");

    public Task OnStepCompletedAsync(string step) => Record("StepCompleted", step);

    public Task OnBuildSucceededAsync() => Record("BuildSucceeded");

    public async Task OnBuildFailedAsync()
    {
        await Task.Yield();
        await Record("BuildFailed");
    }

    public Task OnBuildCompletedAsync() => Record("BuildCompleted");

    private Task Record(string hook, string? detail = null)
    {
        Calls.Add(detail is null ? hook : $"{hook} {detail}");

        if (hook == FailingHook)
        {
            throw new InvalidOperationException($"Hook {hook}");
        }

        return Task.CompletedTask;
    }
}

[Entry(nameof(Failing))]
internal sealed partial class BuildFailedHookBuild : IOnBuildFailed
{
    public bool Failed { get; private set; }

    public Step Failing() => _ => _
        .Executes(() => throw new InvalidOperationException("Boom"));

    public Task OnBuildFailedAsync()
    {
        Failed = true;
        return Task.CompletedTask;
    }
}
