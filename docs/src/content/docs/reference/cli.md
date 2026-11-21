---
title: The baun tool
description: Create and run builds with the baun command-line tool.
---

The `baun` command-line tool creates and runs builds of both kinds. Install it as a .NET tool:

```shell
dotnet tool install --global Baun.Cli
```

## Creating a build

Run `baun init` in your repository to create a new build:

```shell
baun init                # asks which kind of build to create
baun init --project      # creates build/_build.csproj and build/Build.cs
baun init --single-file  # creates Build.cs
```

The new build has a few sample steps and references the same version of baun as the tool. When `baun init` can't ask, for example in a script, it creates a project build. Creating a build never overwrites existing build files.

`baun init` also creates an empty [`.baun/parameters.json`](../../guides/parameters/), also when it updates an existing build. It uses the nearest existing `.baun` folder in the current folder or its parents, or creates `.baun` in the current folder. It never overwrites an existing `parameters.json`. It also adds `temp/` to `.baun/.gitignore`, so files baun creates while it runs, such as the [plan](#running-a-build) page, stay out of git.

When you create a project build and there is a solution (`.sln` or `.slnx`) in the current folder or up to three levels of subfolders, such as `src/MyApp.sln`, `baun init` asks whether to add `build/_build.csproj` to it. If you agree, it runs `dotnet sln add --in-root`, so the build project sits at the root of the solution. With several solutions, you pick one. When `baun init` can't ask, it leaves the solution alone.

## Running without the .NET SDK

`baun init` also creates bootstrap scripts next to the build, so anyone can run it without installing the .NET SDK or the baun tool first:

```shell
./build.sh Test   # Linux and macOS
./build.cmd Test  # Windows (runs build.ps1)
```

The scripts use an installed SDK when the one `dotnet` picks for their folder is .NET 10 or later. That check follows a [`global.json`](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json) next to them. Otherwise they install an SDK into a `.dotnet` folder next to them with Microsoft's [`dotnet-install`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script) script, and reuse it on later runs. They install the version `global.json` sets, or the latest .NET 10 SDK if there's no `global.json` or it sets no version. Nothing is installed system-wide. If `global.json` selects an SDK older than .NET 10, the scripts stop with an error.

`baun init` adds `.dotnet/` to `.gitignore`. When it creates a project build in a folder without a `.gitignore`, it also adds `bin/` and `obj/`, so the output of the build project stays out of git. An existing `.gitignore` keeps its own rules for these. Inside a git repository, it also stages `build.sh` (and a single-file `Build.cs`) as executable, so they stay executable when committed from Windows. Use `baun init --no-bootstrap` to create only the build.

`baun init` always overwrites existing bootstrap scripts. Run it again in a folder that already has a build to update the scripts to the current version. This leaves the build itself alone.

## Running a build

Run a step from your repository:

```shell
baun Test
```

Step names are case-insensitive, so `baun test` runs the `Test` step. Running `baun` without a step name runs the entry step. The tool looks for a `Build.cs` file or a build project in the current folder and its `build` subfolder, and runs it. You don't need to remember whether a repository uses a single-file or a project build, and CI scripts stay short.

baun uses [Spectre.Console](https://spectreconsole.net) for its output. Each step starts with a heading, messages from `Log` start with their time and level (see [Logging](../../guides/logging/)), failures show the exception, and the build ends with a summary table of every step's status and duration.

The tool passes all arguments to the build, except for its own commands such as `init`, which always run as commands. To run a step with the same name as a command, use `--step`, which works for any step:

```shell
baun --step Init
```

The source generator warns about such steps (`BAUN007`).

Run `baun --plan` to open the steps a run would execute, in order and with their dependencies, without running them. Name steps to see their plan, such as `baun Pack --plan`. baun reads the parameters and checks the requirements as it does for a run, so the plan fails for the same mistakes, such as an unknown step or a missing required parameter. A parameter named `Plan` takes the option over, and `--baun-plan` opens the plan instead (see [Parameters](../../guides/parameters/)).

The plan opens in the browser as a graph of all steps and their dependencies on a canvas. The steps that run are numbered in the order they run, and the others are dimmed. Drag to pan, scroll to zoom and hover a step to highlight the steps that would run for it. baun writes the page to `.baun/temp/plan.html`, overwriting it each time, and `baun init` ignores `.baun/temp` in git. On a CI server, in an SSH session, without a display, with redirected output or when the browser can't start, baun prints the path of the page instead, so you can open it yourself.

Run `baun --help` to list the steps and parameters of the build, with the entry step marked, and `baun --version` to show the version of baun the build uses. These options also work with the bootstrap scripts, such as `./build.sh --help`. In a folder without a build, they show the help and version of the tool.
