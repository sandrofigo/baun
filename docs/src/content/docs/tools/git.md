---
title: git
description: Run git from steps and read information about the repository.
---

`Git` runs git, with `Git.Command` and `Git.RunAsync` like their counterparts on `Tool` (see [Running Tools](../../guides/tools/)):

```csharp
await Git.RunAsync("fetch", "--tags");
```

## Repository Information

`Git` also reads information about the repository:

- `Git.GetCurrentTagAsync()` returns the tag of the commit that is checked out, such as v1.2.3, or null if it has none. With several tags, it returns the one with the highest version.
- `Git.GetCurrentTagsAsync()` returns all tags of the commit that is checked out, with the highest version first, or an empty list if it has none.
- `Git.GetCurrentBranchAsync()` returns the branch that is checked out, such as main, or null when no branch is checked out, such as when a CI server checks out a tag or a pull request.

They take an optional directory in the repository and a cancellation token, and throw a `ToolException` outside a repository. Since they run git, use them in a step rather than as a parameter's initial value. For example, to release the tag of the current commit unless `--tag` is passed:

```csharp
[Parameter("Tag to release. Defaults to the tag of the current commit")]
string? Tag;

Step Pack() => _ => _
    .Executes(async () =>
    {
        Tag ??= await Git.GetCurrentTagAsync();
        // ...
    });
```

## Output

`Git.Logger`, a `GitLogger` by default, writes lines of the standard error that start with `fatal:` or `error:` as errors, and lines that start with `warning:` as warnings. Other lines, such as progress and hints, which git also writes to its standard error, are written as Debug. To change that, see [Tool Loggers](../../guides/tools/#tool-loggers).
