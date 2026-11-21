---
title: Parameters
description: Set values of a build from the command line, environment variables or parameters.json.
---

Mark a field or property of the build class with `[Parameter]` to set it from outside the build, for example a configuration, a version or an API key. baun sets parameters before it runs any step:

```csharp
[Entry(nameof(Pack))]
partial class Build
{
    [Parameter("Configuration to build")]
    string Configuration = "Debug";

    [Parameter("NuGet API key")]
    string? NuGetApiKey;

    [Parameter]
    bool Verbose;

    [Parameter("Projects to pack")]
    string[] Projects = [];

    // ...
}
```

`baun --help` lists the parameters with their descriptions.

## Sources

A parameter is read from these sources. The first one that has a value wins:

1. The command line: `baun Pack --configuration Release --verbose`
2. An environment variable: `CONFIGURATION=Release`
3. `.baun/parameters.json` in the nearest `.baun` folder, searched from the current folder up through its parents:

   ```json
   {
     "Configuration": "Release",
     "Projects": ["src/App", "src/Lib"]
   }
   ```

When no source sets a parameter, it keeps its initial value. `baun init` creates an empty `.baun/parameters.json`.

Commit `parameters.json` for shared defaults, and override them with environment variables or on the command line. Keep secrets such as API keys out of the file, because it's committed, and pass them as environment variables instead, for example from your CI server's secrets:

```yaml
- run: ./build.sh Publish
  env:
    NUGET_API_KEY: ${{ secrets.NUGET_API_KEY }}
```

An empty environment variable counts as not set, because CI servers often pass undefined secrets as empty values.

On the command line, a value follows the option, as in `--configuration Release` or `--configuration=Release`. A bool parameter given alone, such as `--verbose`, is true.

:::tip
A project build shows `parameters.json` in a `.baun` folder of the project, so you can open it from the IDE.
:::

## Names

Names are case-insensitive and ignore dashes and underscores, so a parameter named NuGetApiKey is set by any of these:

- `--nuget-api-key` or `--NuGetApiKey` on the command line
- `NUGET_API_KEY` as an environment variable
- `"nugetApiKey"` in `parameters.json`

To use another name, set it in the attribute: `[Parameter(Name = "nuget-source")]`.

baun doesn't add a prefix to environment variables, so pick names that don't clash with other variables. For example, MSBuild also reads `CONFIGURATION`, so with `dotnet run` it also changes the configuration the build itself is compiled with.

Two parameters whose names match this way are a compiler error (`BAUN011`).

## Types

Parameters can be `string`, `bool`, `char`, any number type, an enum, `Guid`, `Uri`, `TimeSpan`, `AbsolutePath`, [`Solution`, `Project`](../../guides/solutions-and-projects/), a nullable of these, or an array of them. Enum values are case-insensitive.

A relative path for a path, solution or project parameter is relative to the root directory, so `--output artifacts` means the same from any folder. An empty value makes it null instead of the root directory. A solution or project parameter reads its file when it's set, so a missing or invalid file is reported before any step runs.

A parameter field can be `readonly`, such as `[Parameter] readonly string Configuration = "Debug";`, so the build itself can't change it.

## Arrays

An array takes its values from the first source that has any, without merging sources:

- On the command line, repeat the option: `--projects src/App --projects src/Lib`
- In an environment variable, separate them with commas: `PROJECTS=src/App,src/Lib`
- In `parameters.json`, use a JSON array, or a single value for an array with one item

## Errors

baun reports an invalid value, such as `--retries many` for a number, as an error before it runs any step. An unknown key in `parameters.json` is also an error, so typos don't go unnoticed.

The source generator reports parameters it can't set as compiler errors: a const or static field, or a property that is static or has no setter (`BAUN008`), and an unsupported type (`BAUN009`). A parameter outside the build class is never set, and the generator warns about it (`BAUN010`). See [Diagnostics](../../reference/diagnostics/) for every rule.

## Built-in Options

A parameter can't be named Help, Version, H or V, after the options `--help`, `--version`, `-h` and `-v`, or start with Baun, so the built-in options always work (`BAUN011`).

A parameter named after another built-in option, such as Plan, takes it over: `--plan` sets the parameter, and the source generator warns about it (`BAUN012`). Every built-in option also works with the prefix `--baun-`, such as `--baun-plan`, so it stays available, and a script can use that form to work with any build. Add `BAUN012` to `<NoWarn>` in the project file of the build if the parameter takes over the option on purpose:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);BAUN012</NoWarn>
</PropertyGroup>
```

## Required Parameters

A parameter of a non-nullable reference type without an initial value is required, so every run needs a value for it:

```csharp
[Parameter("Solution to build")]
Solution Solution;                  // required

[Parameter("Configuration to build")]
string Configuration = "Release";   // optional, has an initial value

[Parameter("NuGet API key")]
string? NuGetApiKey;                // optional, nullable
```

baun checks required parameters after it reads the parameters and before any step runs. If one has no value, or an empty one, the build fails and names every missing parameter and how to set it:

```text
Parameter Solution is required. Pass --solution, set SOLUTION or add it to .baun/parameters.json.
```

Set shared values such as the solution in `parameters.json`. `baun --help` marks required parameters.

A required parameter doesn't need `= null!`: baun suppresses the nullable warning `CS8618` for parameters. If the build class declares its own constructor, the compiler reports the warning on the constructor, which baun doesn't suppress. Value types such as int and builds without nullable reference types have no required parameters.

## Step Requirements

When only some steps need a parameter, such as an API key for publishing, declare it on those steps with `Requires`:

```csharp
[Parameter("NuGet API key")]
string? NuGetApiKey;

Step Publish() => _ => _
    .DependsOn(Pack)
    .Requires(() => NuGetApiKey)
    .Executes(() => ...);
```

baun checks the requirements of every step it is going to run, including dependencies, before the first step starts. Running `baun Publish` without an API key fails before Pack runs, so a build never stops halfway because of a missing value. Chain `Requires` to require several values. `baun --help` lists what each step requires.

A value is missing if it's null, false, an empty string or an empty array, so a condition such as `.Requires(() => Feeds.Length > 0)` works too.

Because baun checks requirements before the first step, an earlier step of the same run can't set a required value. Check values that steps compute in the step that uses them.
