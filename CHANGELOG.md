# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- `baun init` always overwrites existing bootstrap scripts.

### Removed

- The version stamp in the bootstrap scripts and the warning about outdated scripts when running a build.

## [0.1.0] - 2026-11-14

### Added

- Build pipeline steps are methods on a `partial` build class that return a `Step`, with `DependsOn`, sync or async `Executes`, and an `[Entry]` default step.
- Steps run in dependency order, each at most once, stopping at the first failure. Step names are case-insensitive, and `--step <name>` runs any step by name.
- Source generator that registers steps, adds the entry point and reports mistakes as compiler diagnostics (`BAUN001`, `BAUN002`, ...). `BaunBuild.RunAsync<TBuild>()` supports a hand-written entry point.
- Single-file (`Build.cs`) and project (`build/Build.csproj`) builds, with Spectre.Console output.
- `baun` tool (`Baun.Cli`) that runs builds and creates or updates them with `baun init`, including bootstrap scripts that install the .NET SDK locally when it's missing.

[Unreleased]: https://github.com/sandrofigo/baun/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/sandrofigo/baun/releases/tag/v0.1.0
