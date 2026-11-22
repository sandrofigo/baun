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
    .Property("ContinuousIntegrationBuild", "true")
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
| All except `NuGetPush` | `Configuration`, `Framework`, `Runtime`, `Property` (`-p:Name=Value`), `NoRestore`, `Verbosity` |
| `Build`, `Pack`, `Publish` | `Output` |
| `Test`, `Pack`, `Publish` | `NoBuild` |
| `Test` | `Solution`, `Project` (with Microsoft.Testing.Platform) |
| `Pack` | `Version` |
| `Publish` | `SelfContained` (`--self-contained` or, with `false`, `--no-self-contained`) |
| `NuGetPush` | `ApiKey`, `Source`, `SkipDuplicate` |

`Verbosity` takes a `DotNetVerbosity`: Quiet, Minimal, Normal, Detailed or Diagnostic. `ApiKey` adds the key as a [secret](../../guides/tools/#secrets).

Like with `Tool.Command`, each method returns a new command. The general methods, such as `Arguments`, `Secret` and `AllowFailure`, work on typed commands too, for options without a method of their own (see [Running Tools](../../guides/tools/)).

## Other Commands

For other commands, `DotNet.Command` returns a plain command and `DotNet.RunAsync` runs one, like their counterparts on `Tool`:

```csharp
await DotNet.RunAsync("format", "--verify-no-changes");
```

## Output

All of them write the output with `DotNet.Logger`, a `DotNetLogger` by default. It writes MSBuild diagnostics as errors and warnings, such as `Program.cs(3,1): error CS1002: ; expected` and `warning NETSDK1138: ...`, and does the same for NuGet's error and warning lines. To change that, see [Tool Loggers](../../guides/tools/#tool-loggers).
