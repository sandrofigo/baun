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

baun includes a source generator that finds your steps at compile time. It finds every local function and every static method on `Program` that returns a `Step`, takes each step's name from its function name, and registers it with the build. This avoids runtime reflection, and you don't need to register anything by hand. It also lets baun report problems like circular dependencies as compiler errors.

A `Step` is a delegate. Each step returns a lambda, and baun calls that lambda with a builder that configures the step. The compiler infers the builder's type, so you never write it. You configure the step by chaining calls on the lambda parameter, which is named `_` by convention, and each setting goes on its own line. IntelliSense lists every available option after `_.`. `Executes` accepts both synchronous and `async` lambdas.

### Organizing larger builds

Local functions can only live in `Program.cs`. When a build grows, you can spread its steps across several files. Top-level statements compile into a `partial class Program`, so other files can add steps to it as `static` methods:

```csharp
// Steps/Compile.cs
partial class Program
{
    static Step Clean() => _ => _
        .Executes(() =>
        {
            // delete output directories
        });

    static Step Compile() => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            // dotnet build
        });
}
```

```csharp
// Steps/Test.cs
partial class Program
{
    static Step Test() => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            // dotnet test
        });
}
```

`Program.cs` then contains only the line that starts the build:

```csharp
// Program.cs
return await BaunBuild.RunAsync(args, defaultStep: Test);
```

The source generator finds these steps just like local functions. Steps in any file can depend on each other. A step in another file can't see a local function in `Program.cs`, so once you split a build, move all of its steps into `partial class Program`.

Running a step from the command line:

```shell
dotnet run --project build -- Test
```

## Inspiration

baun is heavily inspired by [NUKE](https://github.com/nuke-build/nuke) and its idea of writing builds as C# code. It aims to keep that idea while targeting modern .NET (10 and above).

## License

baun is licensed under the [MIT License](LICENSE).
