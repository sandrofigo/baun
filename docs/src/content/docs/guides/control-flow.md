---
title: Control Flow
description: Run steps only under conditions, order them, trigger them, go on after failures and skip steps.
---

`DependsOn` makes a step run after the steps it needs. These builder calls control further when and in which order steps run:

| Call | Effect |
| --- | --- |
| `OnlyWhen` | Runs the step only when a condition is true |
| `After`, `Before` | Orders steps that run together, without making them depend on each other |
| `Triggers`, `TriggeredBy` | Adds steps to the run after a step |
| `ProceedAfterFailure` | Lets the build go on when the step fails |
| `AlwaysRuns` | Runs the step even after an earlier step failed |

On the command line, [`--skip`](#skipping-steps) skips steps of a run.

## Conditions

`OnlyWhen` runs a step only when a condition is true, such as a publish step that only runs on a CI server:

```csharp
Step Publish() => _ => _
    .DependsOn(Pack)
    .OnlyWhen(() => GitHubActions.IsRunning)
    .Executes(async () =>
    {
        // push the packages
    });
```

baun checks the condition right before the step starts, after its dependencies ran, so it can use values that earlier steps computed. When the condition is false, baun writes why it skips the step, such as `Skipping Publish because GitHubActions.IsRunning is false`, and the summary shows the step as skipped. Steps that depend on a skipped step still run.

Chain `OnlyWhen` to add several conditions, which must all be true. If a condition throws, the step fails. `baun --help` lists the conditions of each step.

baun checks the [requirements](../../guides/parameters/#step-requirements) of a step with a condition when the step is about to run, after its conditions are true, rather than before the first step. Because of that, a step that only runs in CI doesn't need its API key in a local run.

## Ordering

`After` makes a step run after other steps when they're part of the same run, but it doesn't add them to the run:

```csharp
Step Clean() => _ => _
    .Executes(async () => await DotNet.Clean().RunAsync());

Step Restore() => _ => _
    .After(Clean)
    .Executes(async () => await DotNet.Restore().RunAsync());
```

`baun Restore` only runs Restore, and `baun Restore Clean` runs Clean first. `Before` is the reverse: Clean with `.Before(Restore)` orders the steps the same way. A step whose order goes around in a circle, such as `A` after `B` while `B` depends on `A`, fails the build before any step runs.

## Triggers

`Triggers` adds steps to the run whenever a step runs. They run after it, together with their dependencies:

```csharp
Step Publish() => _ => _
    .DependsOn(Pack)
    .Triggers(Notify)
    .Executes(() => { });

Step Notify() => _ => _
    .Executes(() => { });
```

`baun Publish` runs Pack, Publish and then Notify, while `baun Notify` only runs Notify. `TriggeredBy` is the reverse: Notify with `.TriggeredBy(Publish)` does the same. `baun --help` lists what each step triggers, and the [plan](../../reference/cli/#viewing-the-plan) shows triggers as dotted edges.

## Failures

By default, the first step that fails stops the build, and the remaining steps are skipped.

With `ProceedAfterFailure`, the build goes on when the step fails. Steps that depend on it, directly or through other steps, are skipped, but the others run. The build still fails at the end:

```csharp
Step Lint() => _ => _
    .ProceedAfterFailure()
    .Executes(async () => await DotNet.RunAsync("format", "--verify-no-changes"));
```

A step with `AlwaysRuns` runs even after an earlier step failed, for example to clean up or to upload logs. It runs in its normal place in the order. Its dependencies that didn't run because of the failure aren't run for it, and its conditions still apply:

```csharp
Step UploadLogs() => _ => _
    .After(Test)
    .AlwaysRuns()
    .Executes(() => { });
```

Use [hooks](../../guides/hooks/) to react to a failure without a step, such as `IOnBuildFailed`.

## Skipping Steps

`--skip` skips a step that would run. Repeat it to skip several steps:

```shell
baun Pack --skip Test --skip Clean
```

Without a step, such as at the end of the command, `--skip` skips every step except the named ones, so `baun Pack --skip` only runs Pack. Skipped steps show as skipped in the summary, their requirements aren't checked, and the steps that depend on them still run. An unknown step fails the build before any step runs. Like every built-in option, `--skip` also works as `--baun-skip`.
