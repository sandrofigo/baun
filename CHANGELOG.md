# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `baun init --project` asks whether to add the build project to a solution (`.sln` or `.slnx`) in the current folder or its subfolders.
- The `Baun` and `Baun.Cli` packages are published with symbol packages to the nuget.org symbol server, with Source Link to the source on GitHub.

### Changed

- `baun init --project` names the build project `build/_build.csproj` instead of `build/Build.csproj`, and the bootstrap scripts run `build/_build.csproj`.
- The build summary shows step durations under one second as `<1s` instead of milliseconds.
- Durations are written without a space before the unit, such as `1.2s` and `350ms`.
- Redesigned the console output of builds and the `baun` tool: a blue-purple accent color for commands and prompt selections, ✓/✗ status symbols, a borderless build summary, and themed prompts, help and exceptions. Build configuration errors, such as an unknown step, are shown as a single error line instead of an exception.

## [0.2.0] - 2026-11-15

### Changed

- `baun init` always overwrites existing bootstrap scripts.

### Fixed

- The `Baun` and `Baun.Cli` packages include the README.
- Project builds created by `baun init` set an empty `RootNamespace`, so IDEs no longer warn that the namespace of the `Build` class doesn't match its folder.
- Project builds created by `baun init` include a `.editorconfig`, so IDEs no longer warn about steps without the `private` modifier or about using the `_` parameter in `_ => _`. Like the bootstrap scripts, `baun init` always overwrites it, also in an existing project build.

### Removed

- The version stamp in the bootstrap scripts and the warning about outdated scripts when running a build.

## [0.1.0] - 2026-11-14

### Added

- Build pipeline steps are methods on a `partial` build class that return a `Step`, with `DependsOn`, sync or async `Executes`, and an `[Entry]` default step.
- Steps run in dependency order, each at most once, stopping at the first failure. Step names are case-insensitive, and `--step <name>` runs any step by name.
- Source generator that registers steps, adds the entry point and reports mistakes as compiler diagnostics (`BAUN001`, `BAUN002`, ...). `BaunBuild.RunAsync<TBuild>()` supports a hand-written entry point.
- Single-file (`Build.cs`) and project (`build/Build.csproj`) builds, with Spectre.Console output.
- `baun` tool (`Baun.Cli`) that runs builds and creates or updates them with `baun init`, including bootstrap scripts that install the .NET SDK locally when it's missing.

[Unreleased]: https://github.com/sandrofigo/baun/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/sandrofigo/baun/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/sandrofigo/baun/releases/tag/v0.1.0
