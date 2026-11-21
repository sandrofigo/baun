---
title: Getting Started
description: Install the baun CLI, create a build and run it.
---

## Requirements

- [.NET 10](https://dotnet.microsoft.com/download) or later

## Create Your First Build

1. Install the baun CLI as a global .NET tool:

   ```shell
   dotnet tool install --global Baun.Cli
   ```

   To update the CLI and your build project later, see [Updating baun](../reference/cli/#updating-baun).

2. In the root of your repository, create a build:

   ```shell
   baun init
   ```

   baun asks whether to create a project build (`build/_build.csproj` and `build/Build.cs`) or a single-file build (`Build.cs`). A project build is the recommended choice: it works best with IDEs and lets you split the build across several files as it grows. A [single file](../guides/project-or-single-file/#single-file-builds) is a good fit for small builds. `baun init` also adds [bootstrap scripts](../reference/cli/#running-without-the-net-sdk), so others can run the build without installing anything first.

3. Run the build:

   ```shell
   baun          # runs the entry step, Test
   baun Compile  # runs Compile and the steps it depends on
   ```

   The new build has three sample steps, Clean, Compile and Test, that each print a message. baun runs them in dependency order and ends with a summary of each step's status and duration.

4. Open `build/Build.cs` (or `Build.cs` in a single-file build) and replace the sample steps with your own work. Add a step by adding a method that returns a `Step`. The [Concept](../guides/concept/) section explains how steps work.

5. Commit the build folder (or Build.cs) and the bootstrap scripts. On a CI server, run the same build with `./build.sh Test` or `./build.cmd Test`, or install the tool and run `baun Test`.
