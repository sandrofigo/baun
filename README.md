# ![baun](https://raw.githubusercontent.com/sandrofigo/baun/main/docs/src/assets/baun_logotype_cropped.png)

[![NuGet](https://img.shields.io/nuget/v/Baun.svg)](https://www.nuget.org/packages/Baun) [![NuGet downloads](https://img.shields.io/nuget/dt/Baun.svg)](https://www.nuget.org/packages/Baun)

**baun** is a build pipeline system that lets you write any kind of workflow, such as CI/CD, releases, or local automation, in plain C#.

Inspired by [NUKE](https://github.com/nuke-build/nuke), baun treats your build as a regular C# program. You define steps and their dependencies in C#, and you get the full power of the language and its tooling: IntelliSense, refactoring, debugging, and NuGet packages. You don't have to maintain YAML or shell scripts.

> **Status:** baun is in early development. APIs and features described here may change.

**[Read the documentation at baun.dev](https://baun.dev)**

## Getting Started

baun requires [.NET 10](https://dotnet.microsoft.com/download) or later. Install the `baun` tool, create a build in the root of your repository and run it:

```shell
dotnet tool install --global Baun.Cli
baun init
baun
```

`baun init` creates a build project in the `build` folder, or a single `Build.cs` file, along with bootstrap scripts, so others can run the build with `./build.sh` or `./build.cmd` without installing anything first. See [Getting Started](https://baun.dev/getting-started/) for the details.

## Example

A build is a `partial class` whose steps are methods that return a `Step`. baun runs the steps in dependency order:

```csharp
[Entry(nameof(Test))]
partial class Build
{
    [Parameter("Configuration to build")]
    string Configuration = "Release";

    Step Compile() => _ => _
        .Executes(async () =>
        {
            await DotNet.Build().Configuration(Configuration).RunAsync();
        });

    Step Test() => _ => _
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await DotNet.Test().Configuration(Configuration).RunAsync();
        });
}
```

Running `baun` without arguments starts the entry step, so `Compile` runs first and then `Test`.

## Documentation

- [Concept](https://baun.dev/guides/concept/): steps, dependencies and the entry step
- [Control Flow](https://baun.dev/guides/control-flow/): conditions, ordering, triggers, failure handling and skipping steps
- [Parameters](https://baun.dev/guides/parameters/): set values from the command line, environment variables or `parameters.json`
- [Running Tools](https://baun.dev/guides/tools/): run command-line tools from steps
- [dotnet](https://baun.dev/tools/dotnet/) and [git](https://baun.dev/tools/git/): typed commands for the .NET CLI and git
- [CI](https://baun.dev/guides/ci/): GitHub Actions, releases and changelogs
- [baun CLI](https://baun.dev/reference/cli/): every command and option

## Contributing

See [Development](https://baun.dev/contributing/development/) for how to build and test baun, and [`CHANGELOG.md`](https://github.com/sandrofigo/baun/blob/main/CHANGELOG.md) for the changes in each version.

## License

baun is licensed under the [MIT License](https://github.com/sandrofigo/baun/blob/main/LICENSE).
