# baun

- Test: `dotnet test --solution Baun.slnx` (Microsoft.Testing.Platform runner)
- Add notable changes under `## [Unreleased]` in CHANGELOG.md
- New/changed analyzer rules go in `src/Baun.SourceGenerators/AnalyzerReleases.Unshipped.md`
- Tool abstractions (e.g. `DotNet`) must offer exactly what the real CLI accepts per command: every supported option, nothing unsupported (shared base classes only get options all derived commands accept)
- Keep README.md and the docs site (docs/src/content/docs) in sync with API changes
- Releasing: see .github/RELEASING.md
