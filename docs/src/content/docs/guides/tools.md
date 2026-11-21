---
title: Running Tools
description: Run command-line tools such as dotnet and git from steps.
---

`Tool.RunAsync` runs a command-line tool, such as dotnet or git, writes it and its output to the log, and fails the step if the tool fails:

```csharp
Step Test() => _ => _
    .DependsOn(Compile)
    .Executes(async () =>
    {
        await Tool.RunAsync("dotnet", "test", "--no-build", "--configuration", Configuration);
    });
```

For dotnet and git, baun has [typed commands](#dotnet) with a method for each option.

## Output

baun writes the command as an Information message, followed by the output of the tool: each line of its standard output as a Debug message, and each line of its standard error as a warning, because many tools write progress there:

```text
14:03:52 INF › dotnet test --no-build --configuration Release
```

On consoles without Unicode, the command starts with `>` instead of `›`. The output of a tool is written without colors, because baun reads it instead of the terminal. To change the level of the lines, use a [tool logger](#tool-loggers).

## Arguments and Results

Each argument is passed as it is, so an argument with spaces, such as a path, doesn't need quotes. You can also pass a list of arguments, a working directory and a cancellation token:

```csharp
await Tool.RunAsync("dotnet", arguments, RootDirectory / "src", cancellationToken);
```

Canceling stops the tool, along with the processes it started.

The result has the exit code and the lines of the standard output and standard error, so you can use what a tool writes:

```csharp
ToolResult result = await Tool.RunAsync("git", "rev-parse", "HEAD");
string commit = result.StandardOutput[0];
```

## Building Commands

`Tool.Command` builds a command step by step instead: `Arguments` adds arguments, `WorkingDirectory` sets the directory to run in, and `RunAsync` runs it. Each method returns a new command, so a command can be the base of others:

```csharp
ToolCommand gh = Tool.Command("gh").WorkingDirectory(RootDirectory);

await gh.Arguments("release", "view", tag).RunAsync();
await gh.Arguments("release", "upload", tag, package).RunAsync();
```

## Secrets

`Secret` adds an argument, such as an API key, that is passed to the tool as it is, but written to the log as `***`, both in the command and in the output of the tool:

```csharp
await Tool.Command("dotnet")
    .Arguments("nuget", "push", package)
    .Arguments("--api-key").Secret(NuGetApiKey)
    .Arguments("--source", "https://api.nuget.org/v3/index.json")
    .RunAsync();
// 14:03:52 INF › dotnet nuget push App.1.0.0.nupkg --api-key *** --source https://api.nuget.org/v3/index.json
```

The result still has the output as the tool wrote it, including secrets.

## Exit Codes

If the tool exits with a code other than 0, it throws a `ToolException`, which fails the step. Its message names the tool and the exit code, such as "dotnet failed with exit code 1.", and its `Result` has what the tool wrote.

Some tools answer with their exit code. For example, `git diff --quiet` exits with 1 when there are unstaged changes. `AllowFailure` returns the result for any exit code instead of throwing:

```csharp
ToolResult diff = await Tool.Command("git").Arguments("diff", "--quiet").AllowFailure().RunAsync();
bool hasUnstagedChanges = diff.ExitCode != 0;
```

## Tools That Can't Start

A tool that can't be started throws an `InvalidOperationException` that names the tool and says why, such as that it isn't installed or in the PATH, that the working directory doesn't exist, or that the file isn't executable. That's also the case with `AllowFailure`.

## Tool Loggers

Each line a tool writes goes to a `ToolLogger`, which picks its level. The default one writes the standard output as Debug and the standard error as Warning. The dotnet and git tools below come with loggers of their own that write errors and warnings of the tool as Error and Warning messages.

To change how a tool's output is logged, derive from `ToolLogger`, or from a tool's logger such as `DotNetLogger`, and override `GetLevel`. Call the base method for the lines you don't handle. To skip or change lines, override `Write` instead:

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

Each line has the stream it comes from, standard output or standard error, and its text, with secrets as `***`. The two streams are read at the same time, so a logger can be called from two threads at once: keep it without state, or make it thread-safe.

## dotnet

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

| Commands | Methods |
| --- | --- |
| All except `NuGetPush` | `Configuration`, `Framework`, `Runtime`, `Property` (`-p:Name=Value`), `NoRestore`, `Verbosity` |
| `Build`, `Pack`, `Publish` | `Output` |
| `Test`, `Pack`, `Publish` | `NoBuild` |
| `Test` | `Solution`, `Project` (with Microsoft.Testing.Platform) |
| `Pack` | `Version` |
| `Publish` | `SelfContained` (`--self-contained` or, with `false`, `--no-self-contained`) |
| `NuGetPush` | `ApiKey`, `Source`, `SkipDuplicate` |

`Verbosity` takes a `DotNetVerbosity`: Quiet, Minimal, Normal, Detailed or Diagnostic. `ApiKey` adds the key as a [secret](#secrets).

Like with `Tool.Command`, each method returns a new command. The general methods, such as `Arguments`, `Secret` and `AllowFailure`, work on typed commands too, for options without a method of their own.

For other commands, `DotNet.Command` returns a plain command and `DotNet.RunAsync` runs one, like their counterparts on `Tool`:

```csharp
await DotNet.RunAsync("format", "--verify-no-changes");
```

All of them write the output with `DotNet.Logger`, a `DotNetLogger` by default. It writes MSBuild diagnostics as errors and warnings, such as `Program.cs(3,1): error CS1002: ; expected` and `warning NETSDK1138: ...`, and does the same for NuGet's error and warning lines.

## git

`Git` runs git, with `Git.Command` and `Git.RunAsync` like on `DotNet`, and reads information about the repository:

- `Git.GetCurrentTagAsync()` returns the tag of the commit that is checked out, such as v1.2.3, or null if it has none. With several tags, it returns the one with the highest version.
- `Git.GetCurrentBranchAsync()` returns the branch that is checked out, such as main, or null when no branch is checked out, such as when a CI server checks out a tag or a pull request.

Both take an optional directory in the repository and a cancellation token, and throw a `ToolException` outside a repository. Since they run git, use them in a step rather than as a parameter's initial value. For example, to release the tag of the current commit unless `--tag` is passed:

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

`Git.Logger`, a `GitLogger` by default, writes lines of the standard error that start with `fatal:` or `error:` as errors, and lines that start with `warning:` as warnings. Other lines, such as progress and hints, which git also writes to its standard error, are written as Debug.

## Typed Commands for Other Tools

To give another tool typed commands like `DotNet`, derive from `ToolCommand<TSelf>`, with your command as the type argument. Implement `With`, which wraps a changed command in a new instance of your type, and add methods that call `Arguments`:

```csharp
class GhReleaseCreateCommand(ToolCommand command) : ToolCommand<GhReleaseCreateCommand>(command)
{
    public GhReleaseCreateCommand Title(string title) => Arguments("--title", title);

    public GhReleaseCreateCommand Draft() => Arguments("--draft");

    protected override GhReleaseCreateCommand With(ToolCommand command) => new(command);
}

static class Gh
{
    public static GhReleaseCreateCommand ReleaseCreate(string tag) =>
        new(Tool.Command("gh").Arguments("release", "create", tag));
}
```

```csharp
await Gh.ReleaseCreate("v1.2.3").Title("1.2.3").Draft().RunAsync();
```

Your command gets the general methods, such as `Arguments`, `Secret` and `RunAsync`, from the base class, and its `Command` property returns the plain command it wraps.
