---
title: Logging
description: Write messages from steps with Log.
---

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

A build writes messages of `Debug` and above, which includes the output of tools (see [Running tools](../../guides/tools/)). Pass `--verbosity <level>` to change that, such as `--verbosity information` to hide `Debug` messages, or `--verbosity warning` to write only warnings and errors. Without the option, baun reads the level from the environment variable `BAUN_VERBOSITY`. A parameter named `Verbosity` takes the option over, and `--baun-verbosity` sets the level instead (see [Parameters](../../guides/parameters/)).

baun writes its own errors, such as an unknown step, a missing parameter or the exception of a failing step, as `Error` messages too.
