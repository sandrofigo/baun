---
title: Solutions and Projects
description: Read .sln, .slnx and project files.
---

`Solution.Read` reads a solution file in the `.sln` or `.slnx` format. `GetProjects()` returns its projects, including those in solution folders, sorted by path. `GetProject("App")` returns the project with a name and throws if there's none, while `TryGetProject` doesn't throw. `GlobProjects(patterns)` returns the projects whose name matches any of the patterns, where `*` matches any characters, `?` matches one character, and a pattern that starts with `!` excludes what it matches. Project names are the file names without the extension, such as `App.Tests` for `App.Tests.csproj`, and are compared case-insensitively:

```csharp
[Parameter("Solution to build")]
Solution Solution = Solution.Read(RootDirectory / "App.slnx");

Step Test() => _ => _
    .Executes(() =>
    {
        foreach (Project project in Solution.GlobProjects("*.Tests", "!*.Integration.Tests"))
        {
            // dotnet test project.Path
        }
    });
```

`Project.Read` reads a single project file, such as `Project.Read(RootDirectory / "src" / "App" / "App.csproj")`. Both types have the absolute `Path` of their file, its `Directory` and a `Name`. A project from a solution has that `Solution`, and one read by itself has `null`. Like `AbsolutePath`, a `Solution` or `Project` converts to a `string` with its path implicitly, and two are equal when they have the same path.

As a parameter, a path to a solution or project file becomes a `Solution` or `Project`: `baun Test --solution src/Other.sln`.
