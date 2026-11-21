---
title: Solutions and Projects
description: Read .sln, .slnx and project files.
---

`Solution.Read` reads a solution file in the `.sln` or `.slnx` format, so a build can find its projects by name instead of listing paths:

```csharp
[Parameter("Solution to build")]
Solution Solution = Solution.Read(RootDirectory / "App.slnx");

Step Test() => _ => _
    .Executes(async () =>
    {
        foreach (Project project in Solution.GlobProjects("*.Tests", "!*.Integration.Tests"))
        {
            await DotNet.Test().Project(project).RunAsync();
        }
    });
```

## Finding Projects

- `GetProjects()` returns the projects of the solution, including those in solution folders, sorted by path.
- `GetProject("App")` returns the project with a name and throws an `InvalidOperationException` if there's none. `TryGetProject` returns false instead.
- `GlobProjects(patterns)` returns the projects whose name matches any of the patterns. `*` matches any characters, `?` matches one character, and a pattern that starts with `!` excludes what it matches.

Project names are the file names without the extension, such as App.Tests for App.Tests.csproj, and are compared case-insensitively.

## Reading a Project

`Project.Read` reads a single project file, such as `Project.Read(RootDirectory / "src" / "App" / "App.csproj")`.

## Properties

Both types have the absolute `Path` of their file, its `Directory` and a `Name`. A project from a solution has that solution in `Solution`, and one read by itself has null.

Like a path, a solution or project converts to a string with its path implicitly, so you can pass it to tools, and two are equal when they have the same path.

## As Parameters

As a [parameter](../../guides/parameters/), a path to a solution or project file becomes a solution or project: `baun Test --solution src/Other.sln`. A relative path is relative to the root directory, and the file is read when the parameter is set, so a missing or invalid file is reported before any step runs.

## Errors

- A file that doesn't exist throws a `FileNotFoundException`.
- `Solution.Read` throws a `SolutionFormatException` for a file that isn't a .sln or .slnx file, or isn't a valid one.
