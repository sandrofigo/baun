# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Parameters of a non-nullable reference type without an initial value, such as `Solution Solution;`, are required. A missing value is an error before any step runs, and `--help` marks them as required. They don't need `= null!`, because baun suppresses the nullable warning `CS8618` for parameters.
- `Requires` declares a value that a step needs, such as `.Requires(() => NuGetApiKey)`. baun checks the requirements of all steps it is going to run before the first one starts, so a missing value can't stop a build halfway. `--help` lists what each step requires.
- `Log` writes messages from steps with `Log.Verbose`, `Debug`, `Information`, `Warning`, `Error` and `Fatal`. Each line starts with the time and the level, such as `14:03:52 INF Building App`. Messages are message templates, such as `Log.Information("Building {Project}", project.Name)`, whose values are highlighted.
- `--verbosity <level>` sets the lowest level of messages a build writes, such as `--verbosity debug`. Without it, baun reads `BAUN_VERBOSITY`. The default is `Information`.

### Changed

- A parameter of a non-nullable reference type without an initial value, such as `string Configuration;`, is now required, so a build fails if nothing sets it. Before, it kept `null` despite its type. Give it an initial value or make it nullable to keep it optional.
- Outside CI, a step's name is underlined instead of starting with `▸`.
- Build durations are rounded down to whole seconds, such as `12s`, both in the summary and in the line that reports the result. Durations under one second show as `< 1s`.
- baun's errors, such as an unknown step, and the exception of a failing step are written as `Error` messages with the time and level, instead of starting with `✗ error:`.
- The build that `baun init` creates and the examples write with `Log.Information` instead of `Console.WriteLine`.
- A build can't have a parameter named `Verbosity`, because `--verbosity` is now an option of baun.

## [0.6.0] - 2026-11-17

### Added

- `Solution` reads a `.sln` or `.slnx` file with `Solution.Read`. `GetProjects` returns its projects, `GetProject` and `TryGetProject` get a project by name, and `GlobProjects` returns the projects whose name matches patterns such as `*.Tests` and `!*.Integration.Tests`.
- `Project` reads a project file with `Project.Read`. `Solution` and `Project` have the absolute `Path` of their file, its `Directory` and a `Name`, and they are in the new `Baun.DotNet` namespace, which builds import automatically.
- `[Parameter]` fields and properties can be `Solution` and `Project`. A relative value is relative to `RootDirectory`, and a missing or invalid file is an error before any step runs.

### Changed

- `baun init` creates a project build when it can't ask which kind of build to create, such as in a script. Pass `--single-file` for a single-file build.

### Removed

- `LogGroup.Begin`. Builds run their code in steps, which are already log groups, and groups don't nest, so a group opened in a step was only written as a plain line.

### Fixed

- Durations are written with a `.` as the decimal separator in every culture, such as `6.3s` instead of `6,3s`.

## [0.5.0] - 2026-11-16

### Added

- `ChangelogRelease.Link` returns the link of a release at the end of the changelog, such as `[1.2.3]: https://github.com/sandrofigo/baun/compare/v1.2.2...v1.2.3`, or `null` if it has none.
- `AbsolutePath` holds an absolute path of a file or directory. The `/` operator combines it with a relative path, as in `RootDirectory / "artifacts"`, and it converts to a `string` implicitly.
- `[Parameter]` fields and properties can be `AbsolutePath`. A relative value is relative to `RootDirectory`. An empty value is `null`.
- `AbsolutePath` has file system helpers: `Exists`, `FileExists`, `DirectoryExists`, `CreateDirectory`, `CreateOrCleanDirectory`, `Delete`, `CopyTo` and `MoveTo` for files and directories, and `GlobFiles` and `GlobDirectories` with `*`, `**` and `!` exclusion patterns.

### Changed

- `RootDirectory` and `BaunBuild.RootDirectory` are an `AbsolutePath` instead of a `string`.
- `Changelog.Read` takes an `AbsolutePath` instead of a `string`.
- `GitHub.EventPath`, `GitHub.Workspace`, `GitHub.StepSummary`, `GitHub.RunnerTemp` and `GitHub.RunnerToolCache` are an `AbsolutePath` instead of a `string`.
- The build summary shows a step's status as text only, such as `Succeeded`, without a symbol in front.

### Fixed

- Rider no longer rewrites the `build/_build.csproj.DotSettings` from `baun init --project` when it opens the project.

## [0.4.0] - 2026-11-15

### Added

- `GitHub`, in the new `Baun.CI` namespace that builds import automatically, reads the GitHub Actions run the build runs in, such as `GitHub.Tag`, `GitHub.RefName`, `GitHub.RefType`, `GitHub.Repository`, `GitHub.Sha` and `GitHub.Token`.
- `GitHub.CreateOrUpdateRelease` creates the GitHub release for a tag, or updates it if it already exists.
- `GitHubRepository` holds the owner and name of a GitHub repository. `GitHub.Repository` returns it, and `GitHubRepository.Parse` reads `owner/name`.
- `Changelog` in `Baun.CI` reads a [Keep a Changelog](https://keepachangelog.com/) file, such as `Changelog.Read()` for `CHANGELOG.md` in the root directory. It has the unreleased section and the releases, and `Changelog.GetReleaseNotes` (or `ChangelogRelease.Notes`) gives a release's entries as Markdown, for example for `GitHub.CreateOrUpdateRelease`.
- On GitHub Actions, Azure Pipelines, GitLab CI/CD and TeamCity, each step is written in a collapsible log group, and `LogGroup.Begin` in `Baun.CI` opens a group from a build. Groups don't nest: a group inside another one is written as a plain line.

### Changed

- The name of each step is written with an empty line before and after it, separating it from the output of the steps.
- `baun init --project` also writes `build/_build.csproj.DotSettings` with the settings of `build/.editorconfig` for Rider and ReSharper, and both files let private fields be PascalCase, so `[Parameter]` fields such as `NuGetApiKey` aren't flagged by the naming check.
- Project builds no longer list `*.csproj.DotSettings`, `.editorconfig`, `Directory.Build.props` and `Directory.Build.targets` as project items.

## [0.3.0] - 2026-11-15

### Added

- `baun init --project` asks whether to add the build project to a solution (`.sln` or `.slnx`) in the current folder or its subfolders.
- The `Baun` and `Baun.Cli` packages are published with symbol packages to the nuget.org symbol server, with Source Link to the source on GitHub.
- `baun --help` (or `-h`) in a build lists its steps and options, and `baun --version` (or `-v`) shows the version of baun the build uses, instead of failing with "Unknown option".
- `[Parameter]` fields and properties on the build class are set from the command line (`--configuration Release`), environment variables (`CONFIGURATION`) or `.baun/parameters.json` in the nearest `.baun` folder of the current folder or its parents, in that order of precedence. Supports strings, booleans, numbers, enums, `Guid`, `Uri`, `TimeSpan`, nullables and arrays of these. New diagnostics `BAUN008` to `BAUN011` report parameters baun can't set.
- Build classes have a `RootDirectory` property, added by the source generator, with the nearest folder that contains `.baun`. `BaunBuild.RootDirectory` returns the same folder outside the build class.
- `baun init` creates an empty `.baun/parameters.json` if it doesn't exist, in the nearest `.baun` folder or in the current folder.

### Changed

- `baun init --project` names the build project `build/_build.csproj` instead of `build/Build.csproj`, and the bootstrap scripts run `build/_build.csproj`.
- The build summary shows step durations under one second as `<1s` instead of milliseconds.
- Durations are written without a space before the unit, such as `1.2s` and `350ms`.
- Redesigned the console output of builds and the `baun` tool: a blue-purple accent color for commands and prompt selections, ✓/✗ status symbols, a borderless build summary, and themed prompts, help and exceptions. Build configuration errors, such as an unknown step, are shown as a single error line instead of an exception.

## [0.2.0] - 2026-11-15

### Changed

- `baun init` always overwrites existing bootstrap scripts.

### Removed

- The version stamp in the bootstrap scripts and the warning about outdated scripts when running a build.

### Fixed

- The `Baun` and `Baun.Cli` packages include the README.
- Project builds created by `baun init` set an empty `RootNamespace`, so IDEs no longer warn that the namespace of the `Build` class doesn't match its folder.
- Project builds created by `baun init` include a `.editorconfig`, so IDEs no longer warn about steps without the `private` modifier or about using the `_` parameter in `_ => _`. Like the bootstrap scripts, `baun init` always overwrites it, also in an existing project build.

## [0.1.0] - 2026-11-14

### Added

- Build pipeline steps are methods on a `partial` build class that return a `Step`, with `DependsOn`, sync or async `Executes`, and an `[Entry]` default step.
- Steps run in dependency order, each at most once, stopping at the first failure. Step names are case-insensitive, and `--step <name>` runs any step by name.
- Source generator that registers steps, adds the entry point and reports mistakes as compiler diagnostics (`BAUN001`, `BAUN002`, ...). `BaunBuild.RunAsync<TBuild>()` supports a hand-written entry point.
- Single-file (`Build.cs`) and project (`build/Build.csproj`) builds, with Spectre.Console output.
- `baun` tool (`Baun.Cli`) that runs builds and creates or updates them with `baun init`, including bootstrap scripts that install the .NET SDK locally when it's missing.

[Unreleased]: https://github.com/sandrofigo/baun/compare/v0.6.0...HEAD
[0.6.0]: https://github.com/sandrofigo/baun/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/sandrofigo/baun/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/sandrofigo/baun/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/sandrofigo/baun/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/sandrofigo/baun/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/sandrofigo/baun/releases/tag/v0.1.0
