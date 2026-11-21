# ![baun](https://raw.githubusercontent.com/sandrofigo/baun/main/docs/src/assets/baun_logotype.png)

[![NuGet](https://img.shields.io/nuget/v/Baun.svg)](https://www.nuget.org/packages/Baun) [![NuGet downloads](https://img.shields.io/nuget/dt/Baun.svg)](https://www.nuget.org/packages/Baun)

**baun** is a build pipeline system that lets you write any kind of workflow, such as CI/CD, releases, or local automation, in plain C#.

Inspired by [NUKE](https://github.com/nuke-build/nuke), baun treats your build as a regular C# program. You define steps and their dependencies in C#, and you get the full power of the language and its tooling: IntelliSense, refactoring, debugging, and NuGet packages. You don't have to maintain YAML or shell scripts.

> **Status:** baun is in early development. APIs and features described here may change.

## Goals

- **Builds as code**: Define pipelines in strongly typed C# instead of YAML, PowerShell, or Bash.
- **Any workflow**: Use it for CI/CD, packaging, publishing, code generation, or any other repeatable task.
- **Project or single file**: Use a regular build project that grows with your repository, or keep a small build in one `Build.cs` file. Run either with `baun Test`.
- **Simple by default**: Getting started takes little ceremony, and complex scenarios are still possible.
- **Runs anywhere**: Run the same build locally and on any CI server, with the same results.
- **First-class tooling**: Debug your build like any other .NET application.

## Requirements

- [.NET 10](https://dotnet.microsoft.com/download) or later

## Getting Started

1. Install the `baun` tool as a global .NET tool:

   ```shell
   dotnet tool install --global Baun.Cli
   ```

   To update it later, run `dotnet tool update --global Baun.Cli`.

2. In the root of your repository, create a build:

   ```shell
   baun init
   ```

   baun asks whether to create a project build (`build/_build.csproj` and `build/Build.cs`) or a single-file build (`Build.cs`). A project build is the recommended choice: it works best with IDEs and lets you split the build across several files as it grows. A [single file](#single-file-builds) is a good fit for small builds. `baun init` also adds [bootstrap scripts](#running-without-the-net-sdk), so others can run the build without installing anything first.

3. Run the build:

   ```shell
   baun          # runs the entry step, Test
   baun Compile  # runs Compile and the steps it depends on
   ```

   The new build has three sample steps, `Clean`, `Compile` and `Test`, that each print a message. baun runs them in dependency order and ends with a summary of each step's status and duration.

4. Open `build/Build.cs` (or `Build.cs` in a single-file build) and replace the sample steps with your own work. Add a step by adding a method that returns a `Step`. The [Concept](#concept) section explains how steps work.

5. Commit the `build` folder (or `Build.cs`) and the bootstrap scripts. On a CI server, run the same build with `./build.sh Test` or `./build.cmd Test`, or install the tool and run `baun Test`.

## Concept

A baun build is a small build project that sits next to your code, or, for small builds, a single C# file, `Build.cs`. Inside it you declare **steps**, which are named units of work. You also declare how they depend on each other. baun works out the execution order and runs only the steps that are needed.

`Build.cs` contains a `partial class Build` that holds the steps, and each step is a method that returns a `Step`. You don't write an entry point: baun adds one that runs the build.

A step's name is its method name, so you never repeat a name as a string. Steps refer to each other through these methods, so the compiler catches a misspelled or missing dependency. IDE features such as renaming and "find all references" work on steps too. Each step reads from top to bottom: its name, then its dependencies, then the work it does.

The example `Build.cs` below shows the intended style. The final API may differ.

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
        .Executes(() =>
        {
            // dotnet build
        });

    Step Test() => _ => _
        .DependsOn(Compile)
        .Executes(async () =>
        {
            // dotnet test
        });
}
```

The `[Entry]` attribute on the build class names the step that runs when you don't name a step on the command line. Use `nameof`, so the compiler checks the name and renaming the step updates it. baun reads the command-line arguments itself and sets the process exit code when a step fails.

A `Step` is a delegate. Each step returns a lambda, and baun calls that lambda with a builder that configures the step. The compiler infers the builder's type, so you never write it. You configure the step by chaining calls on the lambda parameter, which is named `_` by convention, and each setting goes on its own line. IntelliSense lists every available option after `_.`. `Executes` accepts both synchronous and `async` lambdas.

Because steps are members of a class, the build class is also the natural place for anything they share, such as settings or version numbers. Use regular fields and properties for these.

Every build class has a `RootDirectory` property with the root directory of the repository, so paths don't depend on the folder the build runs from:

```csharp
Step Clean() => _ => _
    .Executes(() =>
    {
        (RootDirectory / "artifacts").Delete();
    });
```

The root directory is the nearest folder that contains a `.baun` folder, searched from the current folder up through its parents. Without a `.baun` folder, it's the current folder. `baun init` creates `.baun` (see [Parameters](#parameters)). The source generator adds the property, unless the build class already has a member named `RootDirectory`. Outside the build class, use `BaunBuild.RootDirectory`.

For files a build only needs while it runs, such as downloads or generated settings, use the `TempDirectory` property. It's `.baun/temp` in the root directory, which `baun init` adds to `.baun/.gitignore`, or the folder `baun` in the temp directory of the system without a `.baun` folder, and it's created when you use it. Like `Path.GetTempFileName` and `Directory.CreateTempSubdirectory` in .NET, `BaunBuild.CreateTempFile` creates an empty file and `BaunBuild.CreateTempDirectory` an empty directory with a unique name in it:

```csharp
Step Download() => _ => _
    .Executes(() =>
    {
        AbsolutePath settings = BaunBuild.CreateTempFile(".json");       // .baun/temp/x3kq1v0d.json
        AbsolutePath tools = BaunBuild.CreateTempDirectory("download-"); // .baun/temp/download-5ty2nmwa
        AbsolutePath log = TempDirectory / "download.log";
    });
```

`CreateTempFile` adds `.tmp` without an extension. baun doesn't delete temp files, so you can look at them after a run. Delete them yourself when you no longer need them. The source generator adds `TempDirectory` unless the build class already has a member with that name. Outside the build class, use `BaunBuild.TempDirectory`.

`RootDirectory` is an `AbsolutePath`, baun's type for absolute paths of files and directories. The `/` operator combines it with a relative path, which may have several segments and `..`:

```csharp
AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
AbsolutePath Package => ArtifactsDirectory / "packages" / "App.1.0.0.nupkg";
```

An `AbsolutePath` converts to a `string` implicitly, so you can pass it to `File`, `Directory`, `Path` and process arguments. To turn a string into an `AbsolutePath`, cast it: `(AbsolutePath)"/tmp/output"`. The cast throws for a relative path, and `/` throws for a rooted one. `Name`, `Extension` and `Parent` return the parts of a path. Two paths are equal when they name the same file, ignoring case on Windows and macOS, and a trailing separator doesn't count.

`AbsolutePath` also has helpers for common file work in a build:

```csharp
Step Pack() => _ => _
    .Executes(() =>
    {
        ArtifactsDirectory.CreateOrCleanDirectory();

        foreach (AbsolutePath project in RootDirectory.GlobFiles("src/**/*.csproj", "!**/bin/**"))
        {
            // dotnet pack project
        }

        (RootDirectory / "README.md").CopyTo(ArtifactsDirectory / "README.md");
    });
```

- `Exists`, `FileExists` and `DirectoryExists` tell whether something is at the path.
- `CreateDirectory()` creates a directory with its parents. `CreateOrCleanDirectory()` also empties a directory that already exists.
- `Delete()` deletes a file, or a directory with everything in it, including read-only files. It does nothing when the path doesn't exist.
- `CopyTo(target)` copies a file, or a directory with everything in it, and creates the target's parent directories. A directory is merged into an existing one. `MoveTo(target)` moves a file or directory. Both throw instead of replacing an existing file unless you pass `overwrite: true`. The target is the new path, not the folder to put it in, so use `directory / path.Name` to keep the name.
- `GlobFiles(patterns)` and `GlobDirectories(patterns)` return the files or directories below a directory that match any of the patterns, sorted by path. `*` matches any characters in a name, and `**` matches any number of directories. A pattern that starts with `!` excludes what it matches, as in `!**/bin/**`.

baun includes a source generator that completes the `partial class Build` at compile time. It finds every method that returns a `Step`, takes each step's name from its method name, and registers it with the build. This avoids scanning the build with reflection, and you don't need to register anything by hand. It also reports mistakes as compiler errors, such as a build class that isn't `partial` or an `[Entry]` that doesn't name a step. baun checks for circular dependencies before it runs any step.

baun's types, such as `Step`, `Entry` and `BaunBuild`, are in the `Baun` namespace, and the types for CI servers, such as `GitHub`, are in `Baun.CI`, the types for .NET solutions and projects and the `DotNet` tool are in `Baun.DotNet`, the `Git` tool is in `Baun.Git`, and `Log` is in `Baun.Logging`. The Baun package imports all five automatically, so a build never needs `using` lines for baun itself.

The entry point comes from baun's source generator: when a build has no top-level statements and no `Main` method, the generator adds one that runs the build class. To run code before or after the build, write the entry point yourself with `BaunBuild.RunAsync`:

```csharp
Log.Information("Starting");
await BaunBuild.RunAsync<Build>();

[Entry(nameof(Test))]
partial class Build
{
    // ...
}
```

### Hooks

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

## Parameters

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

When no source sets a parameter, it keeps its initial value. Commit `parameters.json` for shared defaults, and override them with environment variables or on the command line. Keep secrets such as API keys out of the file and pass them as environment variables, for example from your CI server's secrets. An empty environment variable counts as not set, because CI servers often pass undefined secrets as empty values. `baun init` creates an empty `.baun/parameters.json`.

Names are case-insensitive and ignore `-` and `_`, so `NuGetApiKey` is set by `--nuget-api-key`, `--NuGetApiKey`, `NUGET_API_KEY` or `"nugetApiKey"`. To use another name, set it in the attribute: `[Parameter(Name = "nuget-source")]`. baun doesn't add a prefix to environment variables, so pick names that don't clash with other variables. For example, MSBuild also reads `CONFIGURATION`, so with `dotnet run` it also changes the configuration the build itself is compiled with.

On the command line, a value follows the option, as in `--configuration Release` or `--configuration=Release`. A `bool` parameter given alone, such as `--verbose`, is `true`.

Parameters can be `string`, `bool`, `char`, any number type, an enum, `Guid`, `Uri`, `TimeSpan`, `AbsolutePath`, [`Solution`, `Project`](#solutions-and-projects), a nullable of these, or an array of them. Enum values are case-insensitive. A relative path for an `AbsolutePath`, `Solution` or `Project` parameter is relative to `RootDirectory`, so `--output artifacts` means the same from any folder. An empty value makes it `null`, not `RootDirectory`. A `Solution` or `Project` parameter reads its file when it's set, so a missing or invalid file is reported before any step runs. An array takes its values from the first source that has any, without merging sources:

- On the command line, repeat the option: `--projects src/App --projects src/Lib`
- In an environment variable, separate them with commas: `PROJECTS=src/App,src/Lib`
- In `parameters.json`, use a JSON array, or a single value for an array with one item

`baun --help` lists the parameters. baun reports an invalid value, such as `--retries many` for an `int`, as an error before it runs any step. An unknown key in `parameters.json` is also an error, so typos don't go unnoticed. A parameter field can be `readonly`, such as `[Parameter] readonly string Configuration = "Debug";`, so the build itself can't change it. The source generator reports parameters it can't set as compiler errors, such as a `const` field, a property without a setter or an unsupported type (`BAUN008` to `BAUN011`).

A parameter can't be named after `--help` or `--version`, or start with `Baun`, so the built-in options always work (`BAUN011`). A parameter named after another built-in option, such as `Plan`, takes it over: `--plan` sets the parameter, and the source generator warns about it (`BAUN012`). Every built-in option also works with the prefix `--baun-`, such as `--baun-plan`, so it stays available, and a script can use that form to work with any build. Add `BAUN012` to `<NoWarn>` in the project file of the build if the parameter takes over the option on purpose.

### Required Parameters

A parameter of a non-nullable reference type without an initial value is required, so every run needs a value for it:

```csharp
[Parameter("Solution to build")]
Solution Solution;                  // required

[Parameter("Configuration to build")]
string Configuration = "Release";   // optional, has an initial value

[Parameter("NuGet API key")]
string? NuGetApiKey;                // optional, nullable
```

baun checks required parameters after it reads the parameters and before any step runs. If one has no value, or an empty one, the build fails and names every missing parameter and how to set it: `Parameter Solution is required. Pass --solution, set SOLUTION or add it to .baun/parameters.json.` Set shared values such as the solution in `parameters.json`. A required parameter doesn't need `= null!`: baun suppresses the nullable warning `CS8618` for parameters. If the build class declares its own constructor, the compiler reports the warning on the constructor, which baun doesn't suppress. Value types such as `int` and builds without nullable reference types have no required parameters.

When only some steps need a parameter, such as an API key for publishing, declare it on those steps with `Requires`:

```csharp
[Parameter("NuGet API key")]
string? NuGetApiKey;

Step Publish() => _ => _
    .DependsOn(Pack)
    .Requires(() => NuGetApiKey)
    .Executes(() => ...);
```

baun checks the requirements of every step it is going to run, including dependencies, before the first step starts. `baun Publish` without an API key fails before `Pack` runs, so a build never stops halfway because of a missing value. A value is missing if it's `null`, `false`, an empty string or an empty array, so a condition such as `.Requires(() => Feeds.Length > 0)` works too. Because of this, an earlier step of the same run can't set a required value; check values that steps compute in the step that uses them. Chain `Requires` to require several values. `baun --help` marks required parameters and lists what each step requires.

## Logging

Write messages from steps with `Log`, which has a method for each level: `Verbose`, `Debug`, `Information`, `Warning`, `Error` and `Fatal`. Each message is a line that starts with the time and the level:

```csharp
Step Compile() => _ => _
    .Executes(() =>
    {
        Log.Information("Building {Project} in {Configuration}", project.Name, Configuration);
        Log.Warning("Skipping {Count} projects", skipped.Count);
    });
```

```text
14:03:52 INF Building App in Release
14:03:52 WRN Skipping 2 projects
```

Messages are message templates, as in [Serilog](https://serilog.net), which baun uses to write them: name each value in braces and pass the values in the same order, instead of using an interpolated string. The values are highlighted in the output, numbers and dates are written the same in every culture, and strings are written without quotes. Use `{{` and `}}` for literal braces. Each method also takes an exception as its first argument, such as `Log.Error(exception, "Upload of {File} failed", file)`, and writes it after the message. `Log.IsEnabled(LogLevel.Debug)` tells whether a level is written, so you can skip work that only prepares a message.

A build writes messages of `Debug` and above, which includes the output of tools (see [Running tools](#running-tools)). Pass `--verbosity <level>` to change that, such as `--verbosity information` to hide `Debug` messages, or `--verbosity warning` to write only warnings and errors. Without the option, baun reads the level from the environment variable `BAUN_VERBOSITY`. A parameter named `Verbosity` takes the option over, and `--baun-verbosity` sets the level instead (see [Parameters](#parameters)).

baun writes its own errors, such as an unknown step, a missing parameter or the exception of a failing step, as `Error` messages too.

### Running Tools

`Tool.RunAsync` runs a command-line tool, such as `dotnet` or `git`, and writes it to the log as an `Information` message, such as `14:03:52 INF › dotnet test --no-build` (with `>` on consoles without Unicode), followed by its output: each line of its standard output as a `Debug` message, and each line of its standard error as a `Warning`, because many tools write progress there. If the tool exits with a code other than 0, it throws a `ToolException`, which fails the step:

```csharp
Step Test() => _ => _
    .DependsOn(Compile)
    .Executes(async () =>
    {
        await Tool.RunAsync("dotnet", "test", "--no-build", "--configuration", Configuration);
    });
```

Each argument is passed as it is, so an argument with spaces, such as a path, doesn't need quotes. Pass a list of arguments, a working directory and a `CancellationToken` with `Tool.RunAsync("dotnet", arguments, RootDirectory / "src")`. The result has the `ExitCode` and the lines of the `StandardOutput` and `StandardError`, so you can use what a tool writes:

```csharp
ToolResult result = await Tool.RunAsync("git", "rev-parse", "HEAD");
string commit = result.StandardOutput[0];
```

`Tool.Command` builds a command step by step instead: `Arguments` adds arguments, `Secret` adds an argument such as an API key, `WorkingDirectory` sets the directory to run in, and `RunAsync` runs it. A secret is passed to the tool as it is, but written to the log as `***`, both in the command and in the output of the tool. Each method returns a new command, so a command can be the base of others:

```csharp
await Tool.Command("dotnet")
    .Arguments("nuget", "push", package)
    .Arguments("--api-key").Secret(NuGetApiKey)
    .Arguments("--source", "https://api.nuget.org/v3/index.json")
    .RunAsync();
// 14:03:52 INF › dotnet nuget push App.1.0.0.nupkg --api-key *** --source https://api.nuget.org/v3/index.json
```

The result still has the output as the tool wrote it, including secrets.

Some tools answer with their exit code, such as `git diff --quiet`, which exits with 1 when there are unstaged changes. `AllowFailure` returns the result for any exit code instead of throwing a `ToolException`:

```csharp
ToolResult diff = await Tool.Command("git").Arguments("diff", "--quiet").AllowFailure().RunAsync();
bool hasUnstagedChanges = diff.ExitCode != 0;
```

A tool that can't be started throws an `InvalidOperationException` that names the tool and says why, such as that it isn't installed or in the `PATH`, that the working directory doesn't exist, or that the file isn't executable. That's also the case with `AllowFailure`.

The output of a tool is written without colors, because baun reads it instead of the terminal.

### Tool Loggers

Each line a tool writes goes to a `ToolLogger`, which picks its level. The default one writes the standard output as `Debug` and the standard error as `Warning`, as described above. The `dotnet` and `git` tools below come with loggers of their own that write errors and warnings of the tool as `Error` and `Warning` messages. To change how a tool's output is logged, derive from `ToolLogger` (or from a tool's logger, such as `DotNetLogger`) and override `GetLevel`, calling the base method for the lines you don't handle. Override `Write` instead to skip or change lines:

```csharp
class TestLogger : DotNetLogger
{
    protected override LogLevel GetLevel(ToolOutputLine line) =>
        line.Text.StartsWith("failed ") ? LogLevel.Error : base.GetLevel(line);
}
```

Use it for one command with `Logger`, or for every command a tool creates from then on with the tool's `Logger` property, such as `DotNet.Logger`, `Git.Logger` or `Tool.Logger`:

```csharp
await DotNet.Test().Solution(Solution).Logger(new TestLogger()).RunAsync();

DotNet.Logger = new TestLogger();
```

`ToolOutputLine` has the `Stream` the line comes from (`StandardOutput` or `StandardError`) and its `Text`, with secrets as `***`. The two streams are read at the same time, so a logger can be called from two threads at once: keep it without state, or make it thread-safe.

### dotnet

`DotNet` runs the .NET CLI with typed commands: `Build`, `Test`, `Pack`, `Publish`, `Restore`, `Clean` and `NuGetPush`. Each takes the project or solution (optional, except for the package of `NuGetPush`) and has methods for its options, such as `Configuration`, `Framework`, `Runtime`, `Property` (`-p:Name=Value`), `NoRestore` and `Verbosity` for the commands that run MSBuild, `Output` and `NoBuild` where they apply, `Version` for `Pack`, `Solution` and `Project` for `Test` with Microsoft.Testing.Platform, and `ApiKey`, `Source` and `SkipDuplicate` for `NuGetPush`. `ApiKey` adds the key as a secret. Like `Tool.Command`, each method returns a new command, and `Arguments`, `Secret`, `WorkingDirectory`, `Logger` and `AllowFailure` work on typed commands too, for options without a method of their own:

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

For other commands, `DotNet.Command("format", "--verify-no-changes")` returns a `ToolCommand`, and `DotNet.RunAsync(...)` runs one, like `Tool.Command` and `Tool.RunAsync`. All of them write the output with `DotNet.Logger`, a `DotNetLogger` by default, which writes MSBuild diagnostics such as `Program.cs(3,1): error CS1002: ; expected` as `Error` and `warning NETSDK1138: ...` as `Warning`, as well as NuGet's `error:` and `warn :` lines.

### git

`Git` runs git, with `Git.Command(...)` and `Git.RunAsync(...)` like `DotNet`, and reads information about the repository:

- `Git.GetCurrentTagAsync()` returns the tag of the commit that is checked out, such as `v1.2.3`, or `null` if it has none. With several tags, it returns the one with the highest version.
- `Git.GetCurrentBranchAsync()` returns the branch that is checked out, such as `main`, or `null` when no branch is checked out, such as when a CI server checks out a tag or a pull request.

Both take an optional directory in the repository and a `CancellationToken`, and throw a `ToolException` outside a repository. Since they run git, use them in a step rather than as a parameter's initial value, for example to release the tag of the current commit unless `--tag` is passed:

```csharp
[Parameter("Tag to release. Defaults to the tag of the current commit")]
string? Tag;

Step Pack() => _ => _
    .Executes(async () =>
    {
        Tag ??= await Git.GetCurrentTagAsync();
        // ...
    });
```

`Git.Logger`, a `GitLogger` by default, writes lines of the standard error that start with `fatal:` or `error:` as `Error` and `warning:` as `Warning`. Other lines, such as progress and hints, which git also writes to its standard error, are written as `Debug`.

## Solutions and Projects

`Solution.Read` reads a solution file in the `.sln` or `.slnx` format. `GetProjects()` returns its projects, including those in solution folders, sorted by path. `GetProject("App")` returns the project with a name and throws if there's none, while `TryGetProject` doesn't throw. `GlobProjects(patterns)` returns the projects whose name matches any of the patterns, where `*` matches any characters, `?` matches one character, and a pattern that starts with `!` excludes what it matches. Project names are the file names without the extension, such as `App.Tests` for `App.Tests.csproj`, and are compared case-insensitively:

```csharp
[Parameter("Solution to build")]
Solution Solution = Solution.Read(RootDirectory / "App.slnx");

Step Test() => _ => _
    .Executes(() =>
    {
        foreach (Project project in Solution.GlobProjects("*.Tests", "!*.Integration.Tests"))
        {
            // dotnet test project.Path
        }
    });
```

`Project.Read` reads a single project file, such as `Project.Read(RootDirectory / "src" / "App" / "App.csproj")`. Both types have the absolute `Path` of their file, its `Directory` and a `Name`. A project from a solution has that `Solution`, and one read by itself has `null`. Like `AbsolutePath`, a `Solution` or `Project` converts to a `string` with its path implicitly, and two are equal when they have the same path.

As a parameter, a path to a solution or project file becomes a `Solution` or `Project`: `baun Test --solution src/Other.sln`.

## CI

### GitHub Actions

`GitHub` reads the [default environment variables](https://docs.github.com/en/actions/reference/variables-reference#default-environment-variables) of the GitHub Actions workflow run the build runs in, with a property for each, such as `GitHub.Tag`, `GitHub.RefName`, `GitHub.Sha` and `GitHub.Token`:

```csharp
Step Pack() => _ => _
    .Executes(async () =>
    {
        if (GitHub.Tag is { } tag)
        {
            // runs for a tag, such as v1.2.3
        }
    });
```

Properties with a path, such as `GitHub.Workspace`, `GitHub.StepSummary` and `GitHub.RunnerTemp`, return an `AbsolutePath`. A property is `null` (or `false`) when its variable isn't set or is empty, such as outside GitHub Actions. The variables are read each time a property is used, so you can set them yourself to try a run locally: `GITHUB_REF_TYPE=tag GITHUB_REF_NAME=v1.2.3 baun Pack`. To also accept a value on the command line, use the property as a parameter's initial value:

```csharp
[Parameter("Tag to release")]
string? Tag = GitHub.Tag;
```

`GitHub.CreateOrUpdateRelease` creates the GitHub release for a tag, or updates its title, notes and prerelease flag if it already exists, so the step can be run again. The repository defaults to `GitHub.Repository`, the token to `GitHub.Token`, the title to the tag, and the API URL to `GitHub.ApiUrl`. Pass `token:` to use another token. To release to another repository, pass `repository: new GitHubRepository("octocat", "Hello-World")` or `GitHubRepository.Parse("octocat/Hello-World")`:

```csharp
Step Release() => _ => _
    .Executes(async () =>
    {
        GitHubRelease release = await GitHub.CreateOrUpdateRelease(GitHub.Tag!, notes: "- Fixed a bug");
        Log.Information("Released {Url}", release.Url);
    });
```

The token needs permission to write releases. GitHub Actions doesn't set `GITHUB_TOKEN` as an environment variable by itself, so pass the token of the run to the step that runs the build:

```yaml
permissions:
  contents: write

steps:
  - run: ./build.sh Release
    env:
      GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

### Changelog

`Changelog` reads a changelog in the [Keep a Changelog](https://keepachangelog.com/) format. `Changelog.Read()` reads `CHANGELOG.md` in `RootDirectory`. Pass an `AbsolutePath` to read another file, such as `Changelog.Read(RootDirectory / "docs" / "CHANGES.md")`, or use `Changelog.Parse(text)` for text. An invalid file throws a `ChangelogFormatException` that names the line.

A changelog has the `Unreleased` section, its `Releases` in the order of the file, and the `LatestRelease` (the one with the highest version). `GetRelease("1.2.3")` returns a release and throws if there's none, while `TryGetRelease` doesn't throw. Each `ChangelogRelease` has its `Version`, `Date`, `IsYanked`, the URL of its `Link` from the end of the file (`https://github.com/...` from `[1.2.3]: https://github.com/...`), or `null` if it has none, and `Categories`, such as `Added` and `Fixed`, with their `Entries`. `GetReleaseNotes("1.2.3")` (or a release's `Notes`) returns the categories with entries as Markdown, in the order of Keep a Changelog and without the release heading, so they work as the notes of a GitHub release:

```csharp
Step Release() => _ => _
    .Executes(async () =>
    {
        string tag = GitHub.Tag!;
        string notes = Changelog.Read().GetReleaseNotes(tag.TrimStart('v'));

        await GitHub.CreateOrUpdateRelease(tag, notes: notes);
    });
```

### Log Groups

On a CI server that supports log groups, baun writes each step in a collapsible group named after the step. The summary and the error of a failing step are written outside the groups, so they stay visible. baun detects the CI server from its environment variables:

| CI server | Detected by |
| --- | --- |
| GitHub Actions | `GITHUB_ACTIONS=true` |
| Azure Pipelines | `TF_BUILD=True` |
| GitLab CI/CD | `GITLAB_CI=true` |
| TeamCity | `TEAMCITY_VERSION` |

Outside CI, steps are written as plain lines.

## Project or Single File

baun supports two ways to set up a build. Both use the same API and the same `Build.cs` entry point, so you can switch between them at any time. A project build is the recommended choice for most repositories. A single-file build suits small builds that fit comfortably in one file.

| | Project | Single file |
| --- | --- | --- |
| Files | `Build.cs`, a `.csproj`, and any other files you add | Only `Build.cs` |
| Package reference | `PackageReference` in the `.csproj` | `#:package` line in `Build.cs` |
| Steps | `partial class Build` spread across any number of files | `partial class Build` in `Build.cs` |
| Run | `baun Test` | `baun Test` |

### Project Builds

A project build is a regular .NET console project, for example in a `build` folder, that references baun as a NuGet package:

```xml
<!-- build/_build.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace></RootNamespace>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Baun" Version="0.6.0" />
  </ItemGroup>

</Project>
```

The build project keeps its settings to itself, so the settings of your repository can't break it. `baun init` creates an empty `build/Directory.Build.props` and `build/Directory.Build.targets`, so the build project doesn't import the files of the same name in your repository, with settings such as `TreatWarningsAsErrors`, analyzers or package metadata. Add settings for the build to these files, since `baun init` never overwrites them. `ManagePackageVersionsCentrally` is off, so the version of Baun stays in the project file, also when your repository manages package versions centrally in a `Directory.Packages.props`.

`build/Build.cs` holds the build class, as in the [Concept](#concept) section. `baun init` also adds a `build/.editorconfig` and, for Rider and ReSharper, a `build/_build.csproj.DotSettings`, so IDEs don't ask for `private` on steps, warn about using the `_` parameter in `_ => _` or flag PascalCase parameter fields such as `NuGetApiKey`. Like the bootstrap scripts, `baun init` always overwrites these files. The Baun package hides these files, along with `Directory.Build.props` and `Directory.Build.targets`, from the project's items, so they don't clutter the project in IDEs. Without the [baun tool](#the-baun-tool), you run a step with the .NET SDK directly:

```shell
dotnet run --project build -- Test
```

### Organizing Larger Builds

In a project build, you can spread the build class across as many files as you like, because it's `partial`. For example, keep the main steps in `Build.cs` and the deployment steps in `DeploySteps.cs`:

```csharp
// Build.cs
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
        .Executes(() =>
        {
            // dotnet build
        });

    Step Test() => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            // dotnet test
        });

    Step Pack() => _ => _
        .DependsOn(Test)
        .Executes(() =>
        {
            // dotnet pack
        });
}
```

```csharp
// DeploySteps.cs
partial class Build
{
    Step DeployStaging() => _ => _
        .DependsOn(Pack)
        .Executes(() =>
        {
            // deploy to staging
        });

    Step DeployProduction() => _ => _
        .DependsOn(DeployStaging)
        .Executes(() =>
        {
            // deploy to production
        });
}
```

All parts form one class, so steps in any file can depend on steps in any other file without a prefix. The source generator finds steps in every part.

### Single-File Builds

For a small build with a handful of steps, a single file is a valid alternative to a project. Single-file builds use the file-based apps introduced in .NET 10: the `#:package` line at the top of `Build.cs` references baun, and the .NET SDK takes care of the rest. You don't need a `.csproj` file, a solution, or a separate build folder. The rest of `Build.cs` looks the same as in a project build.

Without the [baun tool](#the-baun-tool), you run a step with the .NET SDK directly:

```shell
dotnet run Build.cs -- Test
```

On Linux and macOS you can also make the build directly executable. Add a shebang as the first line of `Build.cs`:

```csharp
#!/usr/bin/env dotnet
#:package Baun@0.6.0
```

Then mark the file as executable and run it:

```shell
chmod +x Build.cs
./Build.cs Test
```

When a single-file build outgrows one file, turn it into a project with `dotnet project convert Build.cs`.

## The baun Tool

The `baun` command-line tool creates and runs builds of both kinds. Install it as a .NET tool:

```shell
dotnet tool install --global Baun.Cli
```

### Creating a Build

Run `baun init` in your repository to create a new build:

```shell
baun init                # asks which kind of build to create
baun init --project      # creates build/_build.csproj and build/Build.cs
baun init --single-file  # creates Build.cs
```

The new build has a few sample steps and references the same version of baun as the tool. When `baun init` can't ask, for example in a script, it creates a project build. Creating a build never overwrites existing build files.

`baun init` also creates an empty [`.baun/parameters.json`](#parameters), also when it updates an existing build. It uses the nearest existing `.baun` folder in the current folder or its parents, or creates `.baun` in the current folder. It never overwrites an existing `parameters.json`. It also adds `temp/` to `.baun/.gitignore`, so files baun creates while it runs, such as the [plan](#running-a-build) page, stay out of git.

When you create a project build and there is a solution (`.sln` or `.slnx`) in the current folder or up to three levels of subfolders, such as `src/MyApp.sln`, `baun init` asks whether to add `build/_build.csproj` to it. If you agree, it runs `dotnet sln add --in-root`, so the build project sits at the root of the solution. With several solutions, you pick one. When `baun init` can't ask, it leaves the solution alone.

### Running Without the .NET SDK

`baun init` also creates bootstrap scripts next to the build, so anyone can run it without installing the .NET SDK or the baun tool first:

```shell
./build.sh Test   # Linux and macOS
./build.cmd Test  # Windows (runs build.ps1)
```

The scripts use an installed SDK when the one `dotnet` picks for their folder is .NET 10 or later. That check follows a [`global.json`](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json) next to them. Otherwise they install an SDK into a `.dotnet` folder next to them with Microsoft's [`dotnet-install`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script) script, and reuse it on later runs. They install the version `global.json` sets, or the latest .NET 10 SDK if there's no `global.json` or it sets no version. Nothing is installed system-wide. If `global.json` selects an SDK older than .NET 10, the scripts stop with an error.

`baun init` adds `.dotnet/` to `.gitignore`. When it creates a project build in a folder without a `.gitignore`, it also adds `bin/` and `obj/`, so the output of the build project stays out of git. An existing `.gitignore` keeps its own rules for these. Inside a git repository, it also stages `build.sh` (and a single-file `Build.cs`) as executable, so they stay executable when committed from Windows. Use `baun init --no-bootstrap` to create only the build.

`baun init` always overwrites existing bootstrap scripts. Run it again in a folder that already has a build to update the scripts to the current version. This leaves the build itself alone.

### Running a Build

Run a step from your repository:

```shell
baun Test
```

Step names are case-insensitive, so `baun test` runs the `Test` step. Running `baun` without a step name runs the entry step. The tool looks for a `Build.cs` file or a build project in the current folder and its `build` subfolder, and runs it. You don't need to remember whether a repository uses a single-file or a project build, and CI scripts stay short.

baun uses [Spectre.Console](https://spectreconsole.net) for its output. Each step starts with a heading, messages from `Log` start with their time and level (see [Logging](#logging)), failures show the exception, and the build ends with a summary table of every step's status and duration.

The tool passes all arguments to the build, except for its own commands such as `init`, which always run as commands. To run a step with the same name as a command, use `--step`, which works for any step:

```shell
baun --step Init
```

The source generator warns about such steps (`BAUN007`).

Run `baun --plan` to open the steps a run would execute, in order and with their dependencies, without running them. Name steps to see their plan, such as `baun Pack --plan`. baun reads the parameters and checks the requirements as it does for a run, so the plan fails for the same mistakes, such as an unknown step or a missing required parameter. A parameter named `Plan` takes the option over, and `--baun-plan` opens the plan instead (see [Parameters](#parameters)).

The plan opens in the browser as a graph of all steps and their dependencies on a canvas. The steps that run are numbered in the order they run, and the others are dimmed. Drag to pan, scroll to zoom and hover a step to highlight the steps that would run for it. baun writes the page to `.baun/temp/plan.html`, overwriting it each time, and `baun init` ignores `.baun/temp` in git. On a CI server, in an SSH session, without a display, with redirected output or when the browser can't start, baun prints the path of the page instead, so you can open it yourself.

Run `baun --help` to list the steps and parameters of the build, with the entry step marked, and `baun --version` to show the version of baun the build uses. These options also work with the bootstrap scripts, such as `./build.sh --help`. In a folder without a build, they show the help and version of the tool.

## Development

The source code is in the `src` folder:

- `src/Baun`: the library that builds reference.
- `src/Baun.SourceGenerators`: the source generator, shipped inside the `Baun` package.
- `src/Baun.Cli`: the `baun` command-line tool, published as the `Baun.Cli` package.

Build and test everything with the .NET 10 SDK. The tests use [xUnit.net v3](https://xunit.net) on Microsoft Testing Platform, which [`global.json`](global.json) enables for `dotnet test`:

```shell
dotnet test --solution Baun.slnx
```

The documentation site is a [Starlight](https://starlight.astro.build) project in the `docs` folder. Preview it with Node.js:

```shell
cd docs
npm install
npm run dev
```

### Publishing

The [`Release`](.github/workflows/release.yml) workflow publishes the `Baun` and `Baun.Cli` packages to nuget.org with [trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing), so no long-lived API key is stored in the repository. It runs when a version tag is pushed, and the tag sets the version: the tag `v1.2.3` publishes version `1.2.3`, and the build then creates the GitHub release. See [Releasing baun](.github/RELEASING.md) for the steps, and [`CHANGELOG.md`](CHANGELOG.md) for the changes in each version.

## Inspiration

baun is heavily inspired by [NUKE](https://github.com/nuke-build/nuke) and its idea of writing builds as C# code. It aims to keep that idea while targeting modern .NET (10 and above).

## License

baun is licensed under the [MIT License](LICENSE).
