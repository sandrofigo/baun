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

For [dotnet](../../tools/dotnet/) and [git](../../tools/git/), baun has typed commands with a method for each option.

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

Each line a tool writes goes to a `ToolLogger`, which picks its level. The default one writes the standard output as Debug and the standard error as Warning. The [dotnet](../../tools/dotnet/#output) and [git](../../tools/git/#output) tools come with loggers of their own that write errors and warnings of the tool as Error and Warning messages.

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

## Typed Commands for Other Tools

To give another tool typed commands like [`DotNet`](../../tools/dotnet/), derive from `ToolCommand<TSelf>`, with your command as the type argument. Implement `With`, which wraps a changed command in a new instance of your type, and add methods that call `Arguments`:

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
