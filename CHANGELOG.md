# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Build pipelines written in C#, with all types in the `Baun` namespace, which the package imports automatically: steps are methods on a `partial` build class that return a `Step`, configured with `DependsOn` and synchronous or asynchronous `Executes`.
- Builds run the steps named on the command line, each at most once and in dependency order, and stop at the first failing step.
- The source generator adds the entry point. `BaunBuild.RunAsync<TBuild>()` runs a build from a hand-written entry point instead.
- `[Entry(nameof(...))]` attribute on the build class to choose the step that runs when no step is named.
- Source generator that registers steps at compile time and reports mistakes as compiler errors (`BAUN001`, `BAUN002`, `BAUN004`, `BAUN005`).
- Single-file builds (`Build.cs` with `#:package`) and project builds (`build/Build.csproj`).
- Build output with Spectre.Console: a heading per step, formatted exceptions and a summary table.
- `baun` command-line tool (`Baun.Cli` package) that finds and runs single-file and project builds, and creates new ones with `baun init`.

[Unreleased]: https://github.com/sandrofigo/baun/commits/main
