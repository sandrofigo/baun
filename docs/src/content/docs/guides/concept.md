---
title: Concept
description: Steps, dependencies, the entry step and the source generator.
---

A baun build is a small build project that sits next to your code, or, for small builds, a single C# file, `Build.cs`. Inside it you declare **steps**, which are named units of work, and how they depend on each other. baun works out the execution order and runs only the steps that are needed.

```csharp
[Entry(nameof(Test))]
partial class Build
{
    Step Clean() => _ => _
        .Executes(() =>
        {
            // delete output directories
        });

    Step Compile() => _ => _
        .DependsOn(Clean)
        .Executes(async () =>
        {
            await DotNet.Build().RunAsync();
        });

    Step Test() => _ => _
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await DotNet.Test().RunAsync();
        });
}
```

## Steps

Build.cs contains a partial class named Build that holds the steps, and each step is a method that returns a `Step`. A step's name is its method name, so you never repeat a name as a string. Steps refer to each other through these methods, so the compiler catches a misspelled or missing dependency. IDE features such as renaming and "find all references" work on steps too.

A step is a delegate. Each step returns a lambda, and baun calls that lambda with a builder that configures the step. The compiler infers the builder's type, so you never write it. You configure the step by chaining calls on the lambda parameter, which is named `_` by convention, with each call on its own line, so a step reads from top to bottom: its name, then its dependencies, then the work it does. IntelliSense lists every option after `_.`:

- `DependsOn` names the steps that run first. baun checks for circular dependencies before it runs any step.
- `Executes` sets the work of the step, as a synchronous or an async lambda.
- `Requires` names values the step needs, such as an API key (see [Step Requirements](../../guides/parameters/#step-requirements)).

Because steps are members of a class, the build class is also the natural place for anything they share, such as settings or version numbers. Use regular fields and properties for these.

## The Entry Step

The `[Entry]` attribute on the build class names the step that runs when you don't name a step on the command line. Use nameof, so the compiler checks the name and renaming the step updates it. baun reads the command-line arguments itself and sets the process exit code when a step fails.

## The Source Generator

baun includes a source generator that completes the build class at compile time. It finds every method that returns a step, takes each step's name from its method name, and registers it with the build. This avoids scanning the build with reflection, and you don't need to register anything by hand.

The generator also reports mistakes as compiler errors, such as a build class that isn't partial or an entry attribute that doesn't name a step. The [Diagnostics](../../reference/diagnostics/) page lists them all.

## Namespaces

baun's types are in six namespaces, which the Baun package imports automatically, so a build never needs using directives for baun itself:

| Namespace | Types |
| --- | --- |
| `Baun` | `Step`, `Entry`, `Parameter`, `BaunBuild`, `AbsolutePath`, `Tool` |
| `Baun.CI` | CI servers, log groups and `Changelog` |
| `Baun.CI.GitHub` | GitHub Actions and releases: `GitHub`, `GitHubRelease` and `GitHubRepository` |
| `Baun.DotNet` | `Solution`, `Project` and the `DotNet` tool |
| `Baun.Git` | The `Git` tool |
| `Baun.Logging` | `Log` and `LogLevel` |

## Custom Entry Point

You don't write an entry point: when a build has no top-level statements and no `Main` method, the source generator adds one that runs the build class. To run code before or after the build, write the entry point yourself with `BaunBuild.RunAsync`:

```csharp
Log.Information("Starting");
await BaunBuild.RunAsync<Build>();

[Entry(nameof(Test))]
partial class Build
{
    // ...
}
```

To run code at points of the build itself, such as when a step fails, use [hooks](../../guides/hooks/) instead.
