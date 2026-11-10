# baun

**baun** is a build pipeline system for .NET that lets you write any kind of workflow, such as CI/CD, releases, or local automation, in plain C#.

Inspired by [NUKE](https://github.com/nuke-build/nuke), baun treats your build as a regular .NET project. You define steps and their dependencies in C#, and you get the full power of the language and its tooling: IntelliSense, refactoring, debugging, and NuGet packages. You don't have to maintain YAML or shell scripts.

> **Status:** baun is in early development. APIs and features described here may change.

## Goals

- **Builds as code**: Define pipelines in strongly typed C# instead of YAML, PowerShell, or Bash.
- **Any workflow**: Use it for CI/CD, packaging, publishing, code generation, or any other repeatable task.
- **Simple by default**: Getting started takes little ceremony, and complex scenarios are still possible.
- **Runs anywhere**: Run the same build locally and on any CI server, with the same results.
- **First-class tooling**: Debug your build like any other .NET application.

## Requirements

- [.NET 10](https://dotnet.microsoft.com/download) or later

## Concept

A baun build is a small .NET console project that sits next to your code. Inside it you declare **steps**, which are named units of work. You also declare how they depend on each other. baun works out the execution order and runs only the steps that are needed.

By default you write a baun build without a build class. Like the `Program.cs` of an ASP.NET Core app, it uses top-level statements: a single line starts the build, and below it each step is a local function that returns a `Step`.

A step's name is its function name, so you never repeat a name as a string. Steps refer to each other through these functions, so the compiler catches a misspelled or missing dependency. IDE features such as renaming and "find all references" work on steps too. Each step reads from top to bottom: its name, then its dependencies, then the work it does.

The example below shows the intended style. The final API may differ.

```csharp
// Program.cs
return await BaunBuild.RunAsync(args, defaultStep: Test);

Step Clean() => new()
{
    Executes = () =>
    {
        // delete output directories
    },
};

Step Compile() => new()
{
    DependsOn = [Clean],
    Executes = () =>
    {
        // dotnet build
    },
};

Step Test() => new()
{
    DependsOn = [Compile],
    Executes = async () =>
    {
        // dotnet test
    },
};
```

baun includes a source generator that finds your steps at compile time. It finds every local function that returns a `Step`, takes each step's name from its function name, and registers it with the build. This avoids runtime reflection, and you don't need to register anything by hand. It also lets baun report problems like circular dependencies as compiler errors.

Each step is configured with an object initializer, so default code formatting keeps every setting on its own line. IntelliSense lists every available option after `new() {`. `Executes` accepts both synchronous and `async` lambdas.

Running a step from the command line:

```shell
dotnet run --project build -- Test
```

## Inspiration

baun is heavily inspired by [NUKE](https://github.com/nuke-build/nuke) and its idea of writing builds as C# code. It aims to keep that idea while targeting modern .NET (10 and above).

## License

baun is licensed under the [MIT License](LICENSE).
