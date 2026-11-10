# baun

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

## Concept

A baun build is either a single C# file, `Build.cs`, or a small build project that sits next to your code. Inside it you declare **steps**, which are named units of work. You also declare how they depend on each other. baun works out the execution order and runs only the steps that are needed.

By default you write a baun build without a build class. The entry point is `Build.cs`, which plays the role of `Program.cs` in an ASP.NET Core app. It uses top-level statements: a single line starts the build, and below it each step is a local function that returns a `Step`.

A step's name is its function name, so you never repeat a name as a string. Steps refer to each other through these functions, so the compiler catches a misspelled or missing dependency. IDE features such as renaming and "find all references" work on steps too. Each step reads from top to bottom: its name, then its dependencies, then the work it does.

The example `Build.cs` below shows the intended style. The final API may differ.

```csharp
#:package Baun@0.1.0

await Baun.RunAsync(Test);

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
```

`Baun.RunAsync` takes the default step, which runs when you don't name a step on the command line. baun reads the command-line arguments itself and sets the process exit code when a step fails, so you don't have to pass `args` or `return` anything.

baun includes a source generator that finds your steps at compile time. It finds every local function and every static method in the build that returns a `Step`, takes each step's name from its function name, and registers it with the build. This avoids runtime reflection, and you don't need to register anything by hand. It also lets baun report problems like circular dependencies as compiler errors.

A `Step` is a delegate. Each step returns a lambda, and baun calls that lambda with a builder that configures the step. The compiler infers the builder's type, so you never write it. You configure the step by chaining calls on the lambda parameter, which is named `_` by convention, and each setting goes on its own line. IntelliSense lists every available option after `_.`. `Executes` accepts both synchronous and `async` lambdas.

The baun package adds `using Baun;` to your build automatically, in both single-file and project builds, so a build never needs `using` lines for baun itself.

## Single file or project

baun supports two ways to set up a build. Both use the same API and the same `Build.cs` entry point, so you can switch between them at any time.

| | Single file | Project |
| --- | --- | --- |
| Files | Only `Build.cs` | `Build.cs`, a `.csproj`, and any other files you add |
| Package reference | `#:package` line in `Build.cs` | `PackageReference` in the `.csproj` |
| Steps | Local functions and static classes in `Build.cs` | Local functions in `Build.cs` and static classes in any file |
| Run | `baun Test` | `baun Test` |

### Single-file builds

Single-file builds use the file-based apps introduced in .NET 10: the `#:package` line at the top of `Build.cs` references baun, and the .NET SDK takes care of the rest. You don't need a `.csproj` file, a solution, or a separate build folder.

Without the [baun tool](#running-a-build), you run a step with the .NET SDK directly:

```shell
dotnet run Build.cs -- Test
```

On Linux and macOS you can also make the build directly executable. Add a shebang as the first line of `Build.cs`:

```csharp
#!/usr/bin/env dotnet
#:package Baun@0.1.0
```

Then mark the file as executable and run it:

```shell
chmod +x Build.cs
./Build.cs Test
```

### Project builds

A project build is a regular .NET console project, for example in a `build` folder, that references baun as a NuGet package:

```xml
<!-- build/Build.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Baun" Version="0.1.0" />
  </ItemGroup>

</Project>
```

`Build.cs` looks the same as in a single-file build, without the `#:` lines. Without the [baun tool](#running-a-build), you run a step with the .NET SDK directly:

```shell
dotnet run --project build -- Test
```

To turn an existing single-file build into a project, run `dotnet project convert Build.cs`.

### Organizing larger builds

Local functions can only live in `Build.cs`. To spread a build across several files, use a project build and group your steps into your own static classes, spread across as many files as you like:

```csharp
// Steps/CompileSteps.cs
static class CompileSteps
{
    public static Step Clean() => _ => _
        .Executes(() =>
        {
            // delete output directories
        });

    public static Step Compile() => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            // dotnet build
        });
}
```

```csharp
// Steps/TestSteps.cs
static class TestSteps
{
    public static Step Test() => _ => _
        .DependsOn(CompileSteps.Compile)
        .Executes(() =>
        {
            // dotnet test
        });
}
```

`Build.cs` then contains only the line that starts the build:

```csharp
// Build.cs
await Baun.RunAsync(TestSteps.Test);
```

The source generator finds these steps just like local functions. Steps in different classes refer to each other through the class name, such as `CompileSteps.Compile`. Add `using static CompileSteps;` if you'd rather leave the class name out. A step's name is still its method name, so step names must be unique across the whole build. The source generator reports duplicates as compiler errors. A static class can't see the local functions in `Build.cs`, so once you split a build, move all of its steps into static classes.

## Running a build

The `baun` command-line tool runs builds of both kinds with the same command. Install it as a .NET tool:

```shell
dotnet tool install --global baun
```

Then run a step from your repository:

```shell
baun Test
```

Running `baun` without a step name runs the default step. The tool looks for a `Build.cs` file or a build project in the current folder and its `build` subfolder, and runs it. You don't need to remember whether a repository uses a single-file or a project build, and CI scripts stay short.

## Inspiration

baun is heavily inspired by [NUKE](https://github.com/nuke-build/nuke) and its idea of writing builds as C# code. It aims to keep that idea while targeting modern .NET (10 and above).

## License

baun is licensed under the [MIT License](LICENSE).
