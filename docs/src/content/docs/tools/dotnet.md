---
title: dotnet
description: Run the .NET CLI from steps with typed commands.
---

`DotNet` runs the .NET CLI with typed commands: Build, Test, Pack, Publish, Restore, Clean and NuGetPush. Each takes the project or solution, which is optional except for the package to push, and has methods for its options:

```csharp
await DotNet.Pack(Solution)
    .Configuration(Configuration)
    .Output(ArtifactsDirectory)
    .Version("1.2.3")
    .ContinuousIntegrationBuild()
    .RunAsync();

await DotNet.NuGetPush(package)
    .ApiKey(NuGetApiKey)
    .Source("https://api.nuget.org/v3/index.json")
    .SkipDuplicate()
    .Arguments("--timeout", "600")
    .RunAsync();
```

## Options

| Commands | Methods |
| --- | --- |
| All except `NuGetPush` | `Runtime`, `Property` (`-p:Name=Value`), `Version` (`-p:Version=...`), `ContinuousIntegrationBuild`, `Verbosity` |
| All except `Restore` and `NuGetPush` | `Configuration` |
| `Build`, `Test`, `Publish`, `Clean` | `Framework` |
| `Build`, `Test`, `Pack`, `Publish` | `NoRestore` |
| `Build`, `Pack`, `Publish`, `Clean` | `Output` |
| `Test`, `Pack`, `Publish` | `NoBuild` |
| `Test` | `Solution`, `Project` (with Microsoft.Testing.Platform) |
| `Publish` | `SelfContained` (`--self-contained` or, with `false`, `--no-self-contained`) |
| `NuGetPush` | `ApiKey`, `Source`, `SkipDuplicate` |

`Verbosity` takes a `DotNetVerbosity`: Quiet, Minimal, Normal, Detailed or Diagnostic. `ContinuousIntegrationBuild` sets `-p:ContinuousIntegrationBuild=true`, which makes a release build deterministic, or `false` with `ContinuousIntegrationBuild(false)`. `ApiKey` adds the key as a [secret](../../guides/tools/#secrets).

Each command only has the options that its `dotnet` command accepts, so `DotNet.Restore().Configuration("Release")` doesn't compile, since `dotnet restore` has no `--configuration`.

## Building Once

To restore and build only once, give each step one job and skip that job in the steps after it with `NoRestore` and `NoBuild`:

```csharp
Step Restore() => _ => _
    .Executes(async () => await DotNet.Restore(Solution).RunAsync());

Step Compile() => _ => _
    .DependsOn(Restore)
    .Executes(async () => await DotNet.Build(Solution)
        .Configuration(Configuration)
        .NoRestore()
        .Version(Version)
        .RunAsync());

Step Test() => _ => _
    .DependsOn(Compile)
    .Executes(async () => await DotNet.Test().Solution(Solution).Configuration(Configuration).NoBuild().RunAsync());

Step Pack() => _ => _
    .DependsOn(Compile, Test)
    .Executes(async () => await DotNet.Pack(Solution)
        .Configuration(Configuration)
        .Output(ArtifactsDirectory)
        .NoBuild()
        .Version(Version)
        .RunAsync());
```

The tests then run against the same assemblies that are packed. With `NoBuild`, Pack doesn't compile, so properties that end up in the assemblies, such as `Version` and `ContinuousIntegrationBuild`, go on Compile. Pack still needs `Version` for the version of the packages.

Like with `Tool.Command`, each method returns a new command. The general methods, such as `Arguments`, `Secret` and `AllowFailure`, work on typed commands too, for options without a method of their own (see [Running Tools](../../guides/tools/)).

## Other Commands

For other commands, `DotNet.Command` returns a plain command and `DotNet.RunAsync` runs one, like their counterparts on `Tool`:

```csharp
await DotNet.RunAsync("format", "--verify-no-changes");
```

## Output

All of them write the output with `DotNet.Logger`, a `DotNetLogger` by default. It writes MSBuild diagnostics as errors and warnings, such as `Program.cs(3,1): error CS1002: ; expected` and `warning NETSDK1138: ...`, and does the same for NuGet's error and warning lines. To change that, see [Tool Loggers](../../guides/tools/#tool-loggers).
