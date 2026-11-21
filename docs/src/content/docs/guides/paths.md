---
title: Paths and Files
description: RootDirectory, TempDirectory and AbsolutePath helpers.
---

Every build class has a `RootDirectory` property with the root directory of the repository, so paths don't depend on the folder the build runs from:

```csharp
Step Clean() => _ => _
    .Executes(() =>
    {
        (RootDirectory / "artifacts").Delete();
    });
```

## Root Directory

The root directory is the nearest folder that contains a `.baun` folder, searched from the current folder up through its parents. Without a `.baun` folder, it's the current folder. `baun init` creates `.baun` (see [Parameters](../../guides/parameters/)).

The source generator adds the property, unless the build class already has a member named `RootDirectory`. Outside the build class, use `BaunBuild.RootDirectory`.

## Temp Directory

For files a build only needs while it runs, such as downloads or generated settings, use the `TempDirectory` property. It's `.baun/temp` in the root directory, which `baun init` adds to `.baun/.gitignore`. Without a `.baun` folder, it's the folder `baun` in the temp directory of the system. It's created when you use it.

Like `Path.GetTempFileName` and `Directory.CreateTempSubdirectory` in .NET, `BaunBuild.CreateTempFile` creates an empty file and `BaunBuild.CreateTempDirectory` an empty directory with a unique name in it:

```csharp
Step Download() => _ => _
    .Executes(() =>
    {
        AbsolutePath settings = BaunBuild.CreateTempFile(".json");       // .baun/temp/x3kq1v0d.json
        AbsolutePath tools = BaunBuild.CreateTempDirectory("download-"); // .baun/temp/download-5ty2nmwa
        AbsolutePath log = TempDirectory / "download.log";
    });
```

`CreateTempFile` adds `.tmp` without an extension. baun doesn't delete temp files, so you can look at them after a run. Delete them yourself when you no longer need them.

The source generator adds `TempDirectory` unless the build class already has a member with that name. Outside the build class, use `BaunBuild.TempDirectory`.

## AbsolutePath

`RootDirectory` is an `AbsolutePath`, baun's type for absolute paths of files and directories. The `/` operator combines it with a relative path, which may have several segments and `..`:

```csharp
AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
AbsolutePath Package => ArtifactsDirectory / "packages" / "App.1.0.0.nupkg";
```

- An `AbsolutePath` converts to a `string` implicitly, so you can pass it to `File`, `Directory`, `Path` and process arguments.
- To turn a string into an `AbsolutePath`, cast it: `(AbsolutePath)"/tmp/output"`. The cast throws for a relative path, and `/` throws for a rooted one.
- `Name`, `Extension` and `Parent` return the parts of a path.
- Two paths are equal when they name the same file, ignoring case on Windows and macOS. A trailing separator doesn't count.

## File Helpers

`AbsolutePath` also has helpers for common file work in a build:

```csharp
Step Pack() => _ => _
    .Executes(() =>
    {
        ArtifactsDirectory.CreateOrCleanDirectory();

        foreach (AbsolutePath project in RootDirectory.GlobFiles("src/**/*.csproj", "!**/bin/**"))
        {
            // dotnet pack project
        }

        (RootDirectory / "README.md").CopyTo(ArtifactsDirectory / "README.md");
    });
```

- `Exists`, `FileExists` and `DirectoryExists` tell whether something is at the path.
- `CreateDirectory()` creates a directory with its parents. `CreateOrCleanDirectory()` also empties a directory that already exists.
- `Delete()` deletes a file, or a directory with everything in it, including read-only files. It does nothing when the path doesn't exist.
- `CopyTo(target)` copies a file, or a directory with everything in it, and creates the target's parent directories. A directory is merged into an existing one. `MoveTo(target)` moves a file or directory. Both throw instead of replacing an existing file unless you pass `overwrite: true`. The target is the new path, not the folder to put it in, so use `directory / path.Name` to keep the name.

## Globbing

`GlobFiles(patterns)` and `GlobDirectories(patterns)` return the files or directories below a directory that match any of the patterns, sorted by path:

- `*` matches any characters in a name.
- `**` matches any number of directories.
- A pattern that starts with `!` excludes what it matches, as in `!**/bin/**`.
