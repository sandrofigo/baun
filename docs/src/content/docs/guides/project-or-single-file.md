---
title: Project or Single File
description: The two ways to set up a build.
---

baun supports two ways to set up a build. Both use the same API and the same `Build.cs` entry point, so you can switch between them at any time. A project build is the recommended choice for most repositories. A single-file build suits small builds that fit comfortably in one file.

| | Project | Single file |
| --- | --- | --- |
| Files | `Build.cs`, a `.csproj`, and any other files you add | Only `Build.cs` |
| Package reference | `PackageReference` in the `.csproj` | `#:package` line in `Build.cs` |
| Steps | `partial class Build` spread across any number of files | `partial class Build` in `Build.cs` |
| Run | `baun Test` | `baun Test` |

## Project Builds

A project build is a regular .NET console project, for example in a `build` folder, that references baun as a NuGet package:

```xml
<!-- build/_build.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace></RootNamespace>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Baun" Version="0.7.0" />
  </ItemGroup>

</Project>
```

The build project keeps its settings to itself, so the settings of your repository can't break it. `baun init` creates an empty `Directory.Build.props` and `Directory.Build.targets` in the build folder, so the build project doesn't import the files of the same name in your repository, with settings such as TreatWarningsAsErrors, analyzers or package metadata. Add settings for the build to these files, since init never overwrites them. Central package management is off, so the version of Baun stays in the project file, also when your repository manages package versions centrally in a Directory.Packages.props.

`build/Build.cs` holds the build class, as in the [Concept](../../guides/concept/) section. Init also adds an `.editorconfig` and, for Rider and ReSharper, a `_build.csproj.DotSettings` file, so IDEs don't ask for private on steps, warn about using the parameter in `_ => _` or flag PascalCase parameter fields such as NuGetApiKey. Like the bootstrap scripts, init always overwrites these files. The Baun package hides these files, along with the Directory.Build files, from the project's items, so they don't clutter the project in IDEs. Without the [baun CLI](../../reference/cli/), you run a step with the .NET SDK directly:

```shell
dotnet run --project build -- Test
```

## Organizing Larger Builds

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

## Single-File Builds

For a small build with a handful of steps, a single file is a valid alternative to a project. Single-file builds use the file-based apps introduced in .NET 10: the `#:package` line at the top of `Build.cs` references baun, and the .NET SDK takes care of the rest. You don't need a `.csproj` file, a solution, or a separate build folder. The rest of `Build.cs` looks the same as in a project build.

Without the [baun CLI](../../reference/cli/), you run a step with the .NET SDK directly:

```shell
dotnet run Build.cs -- Test
```

On Linux and macOS you can also make the build directly executable. Add a shebang as the first line of `Build.cs`:

```csharp
#!/usr/bin/env dotnet
#:package Baun@0.7.0
```

Then mark the file as executable and run it:

```shell
chmod +x Build.cs
./Build.cs Test
```

When a single-file build outgrows one file, turn it into a project with `dotnet project convert Build.cs`.
