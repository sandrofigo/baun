---
title: Hooks
description: Run code at points of a build, such as when a step fails.
---

To run code at points of a build, such as when a step fails, implement a hook interface on the build class. Each interface has one method, and a build implements only the ones it needs:

```csharp
[Entry(nameof(Test))]
partial class Build : IOnStepFailed, IOnBuildFailed
{
    public Task OnStepFailedAsync(string step, Exception exception)
    {
        Log.Warning("{Step} failed: {Message}", step, exception.Message);
        return Task.CompletedTask;
    }

    public async Task OnBuildFailedAsync() => await NotifyTeamAsync("The build failed");

    // ...
}
```

| Interface | Runs |
|---|---|
| `IOnBuildCreated` | At the start of a run, before baun reads the arguments and sets the parameters |
| `IOnBuildInitialized` | After the parameters are set and the requirements are checked, right before the first step |
| `IOnStepStarted` | Before the work of each step |
| `IOnStepSucceeded` | After each step that succeeds |
| `IOnStepFailed` | After the step that fails, with its exception |
| `IOnStepCompleted` | After each step, whether it succeeds or fails |
| `IOnBuildSucceeded` | After the last step when every step succeeded |
| `IOnBuildFailed` | After a step fails |
| `IOnBuildCompleted` | After `IOnBuildSucceeded` or `IOnBuildFailed` |

`IOnBuildInitialized` and the build hooks after it only run for a build that starts its steps, not for `--help`, `--plan` or an error such as a missing parameter. The build hooks run before the summary, so what they write appears above it.

An exception in `IOnStepStarted` or `IOnStepSucceeded` fails the step. An exception in any other hook is written as an error and fails the build. If `IOnBuildCreated` or `IOnBuildInitialized` throws, no step runs.
