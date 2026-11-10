# baun

**baun** is a build pipeline system for .NET that lets you write any kind of workflow, such as CI/CD, releases, or local automation, in plain C#.

Inspired by [NUKE](https://github.com/nuke-build/nuke), baun treats your build as a regular .NET project. You define targets and their dependencies in C#, and you get the full power of the language and its tooling: IntelliSense, refactoring, debugging, and NuGet packages. You don't have to maintain YAML or shell scripts.

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

A baun build is a small .NET console project that sits next to your code. Inside it you declare **targets**, which are named units of work. You also declare how they depend on each other. baun works out the execution order and runs only the targets that are needed.

The example below shows the intended style. The final API may differ.

```csharp
class Build : BaunBuild
{
    public static int Main() => Execute<Build>(x => x.Test);

    Target Clean => _ => _
        .Executes(() =>
        {
            // delete output directories
        });

    Target Compile => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            // dotnet build
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            // dotnet test
        });
}
```

Running a target from the command line:

```shell
dotnet run --project build -- Test
```

## Inspiration

baun is heavily inspired by [NUKE](https://github.com/nuke-build/nuke) and its idea of writing builds as C# code. It aims to keep that idea while targeting modern .NET (10 and above).

## License

baun is licensed under the [MIT License](LICENSE).
