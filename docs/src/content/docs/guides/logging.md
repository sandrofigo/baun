---
title: Logging
description: Write messages from steps with Log.
---

Write messages from steps with `Log`, which has a method for each level: Verbose, Debug, Information, Warning, Error and Fatal. Each message is a line that starts with the time and the level:

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

baun writes its own errors, such as an unknown step, a missing parameter or the exception of a failing step, as Error messages too.

## Message Templates

Messages are message templates, as in [Serilog](https://serilog.net), which baun uses to write them: name each value in braces and pass the values in the same order. Prefer templates to interpolated strings, because the values are then highlighted in the output, numbers and dates are written the same in every culture, and strings are written without quotes. A brace inside an interpolated value, such as in JSON, would also be read as a placeholder.

Use `{{` and `}}` for literal braces.

## Exceptions

Each method also takes an exception as its first argument, and writes it after the message:

```csharp
Log.Error(exception, "Upload of {File} failed", file);
```

## Verbosity

A build writes messages of Debug and above, which includes the output of tools (see [Running Tools](../../guides/tools/)). Pass `--verbosity <level>` to change that, such as `--verbosity information` to hide Debug messages, or `--verbosity warning` to write only warnings and errors. The level is one of the six level names, in any case. Another value, such as a number, fails the build before any step runs.

Without the option, baun reads the level from the environment variable `BAUN_VERBOSITY`, which is handy on a CI server:

```yaml
env:
  BAUN_VERBOSITY: information
```

A parameter named Verbosity takes the option over, and `--baun-verbosity` sets the level instead (see [Built-in Options](../../guides/parameters/#built-in-options)).

`Log.IsEnabled(LogLevel.Debug)` tells whether a level is written, so you can skip work that only prepares a message.
