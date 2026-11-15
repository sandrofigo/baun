# baun

[![NuGet](https://img.shields.io/nuget/v/Baun.svg)](https://www.nuget.org/packages/Baun)

**baun** is a build pipeline system for .NET that lets you write any kind of workflow, such as CI/CD, releases, or local automation, in plain C#.

Inspired by [NUKE](https://github.com/nuke-build/nuke), baun treats your build as a regular C# program. You define steps and their dependencies in C#, and you get the full power of the language and its tooling: IntelliSense, refactoring, debugging, and NuGet packages. You don't have to maintain YAML or shell scripts.

> **Status:** baun is in early development. APIs and features described here may change.

## Goals

- **Builds as code**: Define pipelines in strongly typed C# instead of YAML, PowerShell, or Bash.
- **Any workflow**: Use it for CI/CD, packaging, publishing, code generation, or any other repeatable task.
- **Single file or project**: Keep a whole build in one `Build.cs` file, or use a regular build project. Run either with `baun Test`. Both are fully supported.
- **Simple by default**: Getting started takes little ceremony, and complex scenarios are still possible.
- **Runs anywhere**: Run the same build locally and on any CI server, with the same results.
- **First-class tooling**: Debug your build like any other .NET application.

## Requirements

- [.NET 10](https://dotnet.microsoft.com/download) or later

## Getting started

1. Install the `baun` tool as a global .NET tool:

   ```shell
   dotnet tool install --global Baun.Cli
   ```

   To update it later, run `dotnet tool update --global Baun.Cli`.

2. In the root of your repository, create a build:

   ```shell
   baun init
   ```

   baun asks whether to create a single-file build (`Build.cs`) or a project build (`build/_build.csproj` and `build/Build.cs`). If you're not sure, start with a single file. You can [convert it to a project](#project-builds) later. `baun init` also adds [bootstrap scripts](#running-without-the-net-sdk), so others can run the build without installing anything first.

3. Run the build:

   ```shell
   baun          # runs the entry step, Test
   baun Compile  # runs Compile and the steps it depends on
   ```

   The new build has three sample steps, `Clean`, `Compile` and `Test`, that each print a message. baun runs them in dependency order and ends with a summary of each step's status and duration.

4. Open `Build.cs` and replace the sample steps with your own work. Add a step by adding a method that returns a `Step`. The [Concept](#concept) section explains how steps work.

5. Commit `Build.cs` (or the `build` folder) and the bootstrap scripts. On a CI server, run the same build with `./build.sh Test` or `./build.cmd Test`, or install the tool and run `baun Test`.

## Concept

A baun build is either a single C# file, `Build.cs`, or a small build project that sits next to your code. Inside it you declare **steps**, which are named units of work. You also declare how they depend on each other. baun works out the execution order and runs only the steps that are needed.

`Build.cs` contains a `partial class Build` that holds the steps, and each step is a method that returns a `Step`. You don't write an entry point: baun adds one that runs the build.

A step's name is its method name, so you never repeat a name as a string. Steps refer to each other through these methods, so the compiler catches a misspelled or missing dependency. IDE features such as renaming and "find all references" work on steps too. Each step reads from top to bottom: its name, then its dependencies, then the work it does.

The example `Build.cs` below shows the intended style. The final API may differ.

```csharp
#:package Baun@0.2.0

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

baun includes a source generator that completes the `partial class Build` at compile time. It finds every method that returns a `Step`, takes each step's name from its method name, and registers it with the build. This avoids scanning the build with reflection, and you don't need to register anything by hand. It also reports mistakes as compiler errors, such as a build class that isn't `partial` or an `[Entry]` that doesn't name a step. baun checks for circular dependencies before it runs any step.

baun's types, such as `Step`, `Entry` and `BaunBuild`, are in the `Baun` namespace. The Baun package imports it automatically, so a build never needs `using` lines for baun itself.

The entry point comes from baun's source generator: when a build has no top-level statements and no `Main` method, the generator adds one that runs the build class. To run code before or after the build, write the entry point yourself with `BaunBuild.RunAsync`:

```csharp
Console.WriteLine("Starting");
await BaunBuild.RunAsync<Build>();

[Entry(nameof(Test))]
partial class Build
{
    // ...
}
```

## Single file or project

baun supports two ways to set up a build. Both use the same API and the same `Build.cs` entry point, so you can switch between them at any time.

| | Single file | Project |
| --- | --- | --- |
| Files | Only `Build.cs` | `Build.cs`, a `.csproj`, and any other files you add |
| Package reference | `#:package` line in `Build.cs` | `PackageReference` in the `.csproj` |
| Steps | `partial class Build` in `Build.cs` | `partial class Build` spread across any number of files |
| Run | `baun Test` | `baun Test` |

The [`examples`](examples) folder contains a sample of each: a [single-file build](examples/single-file/Build.cs) and a [project build](examples/project/build). They reference the latest Baun package.

### Single-file builds

Single-file builds use the file-based apps introduced in .NET 10: the `#:package` line at the top of `Build.cs` references baun, and the .NET SDK takes care of the rest. You don't need a `.csproj` file, a solution, or a separate build folder.

Without the [baun tool](#the-baun-tool), you run a step with the .NET SDK directly:

```shell
dotnet run Build.cs -- Test
```

On Linux and macOS you can also make the build directly executable. Add a shebang as the first line of `Build.cs`:

```csharp
#!/usr/bin/env dotnet
#:package Baun@0.2.0
```

Then mark the file as executable and run it:

```shell
chmod +x Build.cs
./Build.cs Test
```

### Project builds

A project build is a regular .NET console project, for example in a `build` folder, that references baun as a NuGet package:

```xml
<!-- build/_build.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace></RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Baun" Version="0.2.0" />
  </ItemGroup>

</Project>
```

`Build.cs` looks the same as in a single-file build, without the `#:` lines. `baun init` also adds a `build/.editorconfig`, so IDEs don't ask for `private` on steps or warn about using the `_` parameter in `_ => _`. Like the bootstrap scripts, `baun init` always overwrites this file. Without the [baun tool](#the-baun-tool), you run a step with the .NET SDK directly:

```shell
dotnet run --project build -- Test
```

To turn an existing single-file build into a project, run `dotnet project convert Build.cs`.

### Organizing larger builds

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

## The baun tool

The `baun` command-line tool creates and runs builds of both kinds. Install it as a .NET tool:

```shell
dotnet tool install --global Baun.Cli
```

### Creating a build

Run `baun init` in your repository to create a new build:

```shell
baun init                # asks which kind of build to create
baun init --single-file  # creates Build.cs
baun init --project      # creates build/_build.csproj and build/Build.cs
```

The new build has a few sample steps and references the same version of baun as the tool. When `baun init` can't ask, for example in a script, it creates a single-file build. Creating a build never overwrites existing build files.

When you create a project build and there is a solution (`.sln` or `.slnx`) in the current folder or up to three levels of subfolders, such as `src/MyApp.sln`, `baun init` asks whether to add `build/_build.csproj` to it. If you agree, it runs `dotnet sln add --in-root`, so the build project sits at the root of the solution. With several solutions, you pick one. When `baun init` can't ask, it leaves the solution alone.

### Running without the .NET SDK

`baun init` also creates bootstrap scripts next to the build, so anyone can run it without installing the .NET SDK or the baun tool first:

```shell
./build.sh Test   # Linux and macOS
./build.cmd Test  # Windows (runs build.ps1)
```

The scripts use an installed .NET 10 (or later) SDK when there is one. Otherwise they install the latest .NET 10 SDK into a `.dotnet` folder next to them with Microsoft's [`dotnet-install`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script) script, and reuse it on later runs. Nothing is installed system-wide.

`baun init` adds `.dotnet/` to `.gitignore`. Inside a git repository, it also stages `build.sh` (and a single-file `Build.cs`) as executable, so they stay executable when committed from Windows. Use `baun init --no-bootstrap` to create only the build.

`baun init` always overwrites existing bootstrap scripts. Run it again in a folder that already has a build to update the scripts to the current version. This leaves the build itself alone.

### Running a build

Run a step from your repository:

```shell
baun Test
```

Step names are case-insensitive, so `baun test` runs the `Test` step. Running `baun` without a step name runs the entry step. The tool looks for a `Build.cs` file or a build project in the current folder and its `build` subfolder, and runs it. You don't need to remember whether a repository uses a single-file or a project build, and CI scripts stay short.

baun uses [Spectre.Console](https://spectreconsole.net) for its output. Each step starts with a heading, failures show the exception, and the build ends with a summary table of every step's status and duration.

The tool passes all arguments to the build, except for its own commands such as `init`, which always run as commands. To run a step with the same name as a command, use `--step`, which works for any step:

```shell
baun --step Init
```

The source generator warns about such steps (`BAUN007`).

Run `baun --help` to list the steps of the build, with the entry step marked, and `baun --version` to show the version of baun the build uses. Both also work with the bootstrap scripts, such as `./build.sh --help`. In a folder without a build, they show the help and version of the tool.

## Development

The source code is in the `src` folder:

- `src/Baun`: the library that builds reference.
- `src/Baun.SourceGenerators`: the source generator, shipped inside the `Baun` package.
- `src/Baun.Cli`: the `baun` command-line tool, published as the `Baun.Cli` package.

Build and test everything with the .NET 10 SDK. The tests use [xUnit.net v3](https://xunit.net) on Microsoft Testing Platform, which [`global.json`](global.json) enables for `dotnet test`:

```shell
dotnet test --solution Baun.slnx
```

### Publishing

The [`Release`](.github/workflows/release.yml) workflow publishes the `Baun` and `Baun.Cli` packages to nuget.org with [trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing), so no long-lived API key is stored in the repository. It runs when a GitHub release is published, and the release's tag sets the version: the tag `v1.2.3` publishes version `1.2.3`. See [Releasing baun](docs/releasing.md) for the steps, and [`CHANGELOG.md`](CHANGELOG.md) for the changes in each version.

## Inspiration

baun is heavily inspired by [NUKE](https://github.com/nuke-build/nuke) and its idea of writing builds as C# code. It aims to keep that idea while targeting modern .NET (10 and above).

## License

baun is licensed under the [MIT License](LICENSE).
