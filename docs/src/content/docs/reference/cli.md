---
title: baun CLI
description: Create and run builds with the baun command-line tool.
---

The baun command-line tool creates and runs builds of both kinds. Install it as a .NET tool:

```shell
dotnet tool install --global Baun.Cli
```

## Creating a Build

Run `baun init` in your repository to create a new build:

```shell
baun init                # asks which kind of build to create
baun init --project      # creates build/_build.csproj and build/Build.cs
baun init --single-file  # creates Build.cs
```

The new build has a few sample steps and references the same version of baun as the tool. When init can't ask, for example in a script, it creates a project build. Creating a build never overwrites existing build files.

The command also creates an empty [`.baun/parameters.json`](../../guides/parameters/), also when it updates an existing build. It uses the nearest existing `.baun` folder in the current folder or its parents, or creates one in the current folder, and never overwrites an existing parameters file. It also adds `temp/` to `.baun/.gitignore`, so files baun creates while it runs, such as the [plan](#viewing-the-plan) page, stay out of git.

When you create a project build and there is a solution in the current folder or up to three levels of subfolders, such as `src/MyApp.sln`, init asks whether to add the build project to it. If you agree, it runs `dotnet sln add --in-root`, so the build project sits at the root of the solution. With several solutions, you pick one. When init can't ask, it leaves the solution alone.

## Running Without the .NET SDK

`baun init` also creates bootstrap scripts next to the build, so anyone can run it without installing the .NET SDK or the baun CLI first:

```shell
./build.sh Test   # Linux and macOS
./build.cmd Test  # Windows (runs build.ps1)
```

The scripts use an installed SDK when the one dotnet picks for their folder is .NET 10 or later. That check follows a [`global.json`](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json) next to them. Otherwise they install an SDK into a `.dotnet` folder next to them with Microsoft's [`dotnet-install`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script) script, and reuse it on later runs. They install the version global.json sets, or the latest .NET 10 SDK if there's no global.json or it sets no version. Nothing is installed system-wide. If global.json selects an SDK older than .NET 10, the scripts stop with an error.

It also adds `.dotnet/` to `.gitignore`. When it creates a project build in a folder without a `.gitignore`, it also adds `bin/` and `obj/`, so the output of the build project stays out of git. An existing `.gitignore` keeps its own rules for these. Inside a git repository, it also stages build.sh, and a single-file Build.cs, as executable, so they stay executable when committed from Windows. Use `baun init --no-bootstrap` to create only the build.

It always overwrites existing bootstrap scripts. Run it again in a folder that already has a build to update the scripts to the current version, see [Updating baun](#updating-baun). This leaves the build itself alone.

## Updating baun

The baun CLI and the Baun package your build references are versioned separately, so updating one doesn't update the other. To move an existing build to a new version of baun, update both and then let init regenerate the files it manages:

1. Update the tool:

   ```shell
   dotnet tool update --global Baun.Cli
   ```

2. Update the Baun package of the build. For a project build, this sets the `PackageReference` in the `.csproj` to the latest version, and for a single-file build the `#:package` line in `Build.cs`:

   ```shell
   dotnet package add Baun --project build  # project build
   dotnet package add Baun --file Build.cs  # single-file build
   ```

   Add `--version <version>` to pick a version other than the latest one.

3. In the folder of the build, the root of your repository, run init again:

   ```shell
   baun init
   ```

   In a folder that already has a build, init doesn't create a new one. It updates the files it manages to the version of the tool, so update the tool first.

4. Run `baun --version` to check which version the build now uses, review the changes with `git diff`, and commit them.

Check the [changelog](https://github.com/sandrofigo/baun/blob/main/CHANGELOG.md) for changes that affect your build before you update.

### What Init Updates

In an existing build, `baun init` overwrites these files with the current version:

- The [bootstrap scripts](#running-without-the-net-sdk), `build.sh`, `build.ps1` and `build.cmd`, and stages build.sh as executable in git
- In a project build, `build/.editorconfig` and `build/_build.csproj.DotSettings`, the [IDE settings](../../guides/project-or-single-file/#project-builds)

It also adds what's missing and never overwrites what's there:

- `.dotnet/` in `.gitignore`
- An empty [`.baun/parameters.json`](../../guides/parameters/), when the repository has none
- `temp/` in `.baun/.gitignore`

It leaves your own files alone: `Build.cs`, the `.csproj` and the other files of the build, as well as `build/Directory.Build.props` and `build/Directory.Build.targets`. That's why you update the version of the Baun package yourself, in step 2. If you changed one of the files init overwrites, such as the bootstrap scripts, it overwrites your changes too, so review the diff before you commit.

In an existing build, init takes no options. It fails when you pass `--project`, `--single-file` or `--no-bootstrap`, and when the build isn't in the place the bootstrap scripts expect, a `Build.cs` or `build/_build.csproj` in the current folder.

## Running a Build

Run a step from your repository:

```shell
baun Test
```

Step names are case-insensitive, so `baun test` runs the Test step. Running baun without a step name runs the entry step. Name several steps to run them together, such as `baun Test Pack`: each step runs once, after the steps it depends on. An unknown step fails the build and lists the available steps. The tool looks for a Build.cs file or a build project in the current folder and its build subfolder, and runs it. You don't need to remember whether a repository uses a single-file or a project build, and CI scripts stay short.

baun uses [Spectre.Console](https://spectreconsole.net) for its output. Each step starts with a heading, log messages start with their time and level (see [Logging](../../guides/logging/)), failures show the exception, and the build ends with a summary table of every step's status and duration.

## Options

Every build has these built-in options, next to its own [parameters](../../guides/parameters/):

| Option | Description |
| --- | --- |
| `--help`, `-h` | Lists the steps and parameters of the build, see [Help and Version](#help-and-version) |
| `--version`, `-v` | Shows the version of baun the build uses |
| `--plan` | Opens the steps that would run in the browser without running them, see [Viewing the Plan](#viewing-the-plan) |
| `--step <name>` | Runs a step, also one whose name is a command of the tool, see [Steps Named Like Commands](#steps-named-like-commands) |
| `--skip [<step>]` | Skips a step that would run, and can be repeated. Without a step, skips all steps but the named ones, see [Skipping Steps](../../guides/control-flow/#skipping-steps) |
| `--verbosity <level>` | Sets the lowest level of messages the build writes, see [Verbosity](../../guides/logging/#verbosity) |

Each option also works with the prefix `--baun-`, such as `--baun-plan`, which no parameter can take over (see [Built-in Options](../../guides/parameters/#built-in-options)). An unknown option fails the build before any step runs.

`baun init` takes these options:

| Option | Description |
| --- | --- |
| `--project` | Creates a project build, `build/_build.csproj` and `build/Build.cs` |
| `--single-file` | Creates a single-file build, `Build.cs` |
| `--no-bootstrap` | Doesn't create the [bootstrap scripts](#running-without-the-net-sdk) |

## Steps Named Like Commands

The tool passes all arguments to the build, except for its own commands such as init, which always run as commands. To run a step with the same name as a command, use `--step`, which works for any step:

```shell
baun --step Init
```

The source generator warns about such steps ([BAUN007](../../reference/diagnostics/#baun007)). Step names that differ only in case, such as Test and test, are a compiler error ([BAUN006](../../reference/diagnostics/#baun006)), because the command line matches them case-insensitively.

## Viewing the Plan

Run `baun --plan` to open the steps a run would execute, in order and with their dependencies, without running them. Name steps to see their plan, such as `baun Pack --plan`. baun reads the parameters as it does for a run, so the plan fails for the same mistakes, such as an unknown step, but it doesn't check the requirements of the steps, so a missing required parameter doesn't hide the plan. A parameter named Plan takes the option over, and `--baun-plan` opens the plan instead (see [Built-in Options](../../guides/parameters/#built-in-options)).

The plan opens in the browser as a graph of all steps on a canvas. The steps that run are numbered in the order they run, and the others are dimmed. The lines show how the steps relate, as in the [Control Flow](../../guides/control-flow/) guide: a solid line is a dependency, a dashed line an order from `After` or `Before`, and a dotted line a trigger. Drag to pan, scroll to zoom and hover a step to highlight its flow, the steps that run when you run it on its own, with their dependencies and the steps they trigger. baun writes the page to `.baun/temp/plan.html`, overwriting it each time, and init ignores that folder in git. On a CI server, in an SSH session, without a display, with redirected output or when the browser can't start, baun prints the path of the page instead, so you can open it yourself.

## Help and Version

Run `baun --help` to list the steps and parameters of the build, with the entry step marked, and `baun --version` to show the version of baun the build uses. These options also work with the bootstrap scripts, such as `./build.sh --help`. In a folder without a build, they show the help and version of the tool.
