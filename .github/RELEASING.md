# Releasing baun

Pushing a version tag starts the [`Release`](workflows/release.yml) workflow. It runs the baun build in [`build/`](../build/Build.cs), which validates `CHANGELOG.md`, tests and packs `Baun` and `Baun.Cli` with the tag's version, pushes them to nuget.org with [trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) and creates the GitHub release from the changelog entries.

## One-Time Setup

Already in place. Check these if publishing fails or when setting up a fork.

- **nuget.org trusted publishing policy:** owner `sandrofigo`, repository `baun`, workflow `release.yml`, environment `release`.
- **Repository secret `NUGET_USER`:** the nuget.org profile name (not email) of the policy owner.
- **Environment `release`:** a deployment **tag** rule for `v*` (branch rules don't match). Optionally add required reviewers.

## Release Steps

1. **Pick the version.** Tags must be `v1.2.3` or `v1.2.3-preview.1`; the build rejects anything else. The tag is the only place the version is set (non-tag builds use `0.0.0-dev`).
2. **Update `CHANGELOG.md`** ([Keep a Changelog](https://keepachangelog.com/en/1.1.0/)):
   - Rename `## [Unreleased]` to `## [1.2.3] - YYYY-MM-DD` and add a new empty `## [Unreleased]` above it.
   - Update the links at the bottom:

     ```markdown
     [Unreleased]: https://github.com/sandrofigo/baun/compare/v1.2.3...HEAD
     [1.2.3]: https://github.com/sandrofigo/baun/compare/v1.2.2...v1.2.3
     ```

   - Run `baun FormatChangelog`, then `baun ValidateChangelog`.
3. **Ship the analyzer rules.** Move any rules from `src/Baun.SourceGenerators/AnalyzerReleases.Unshipped.md` to `AnalyzerReleases.Shipped.md` under `## Release 1.2.3` ([format](https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md)), leaving only the header comments in the unshipped file.
4. **Merge into `main`** via a pull request with a passing `Test` workflow. Optionally dry-run the release checks locally with a temporary tag (the build reads the version from the tag on `HEAD`):

   ```shell
   git tag v1.2.3 && baun Pack; git tag -d v1.2.3
   ```

5. **Tag and push** on the day of the changelog date (UTC):

   ```shell
   git switch main && git pull
   git tag v1.2.3
   git push origin v1.2.3
   ```

   Don't create the GitHub release yourself.
6. **Approve** the `publish` job in the `Release` run if the environment requires reviewers.
7. **Check the docs.** After a stable release, the `docs` job deploys [baun.dev](https://baun.dev) from the tag. Preview releases don't deploy the docs.
8. **Verify** with `dotnet tool update --global Baun.Cli --version 1.2.3`, then run `baun init` and `baun` in an empty folder. The new build references the Baun package of the same version, so this checks both packages.

The build fails before publishing if:

- the changelog isn't in the Keep a Changelog format,
- it has no section for the tag's version, or that section isn't the latest release,
- the section isn't dated today (UTC), or is marked `[YANKED]`,
- the section has no entries,
- the comparison link for the version is missing.

## Deploying Docs Fixes Between Releases

The docs site is only deployed on stable releases, so docs for unreleased features on `main` stay unpublished. To ship a fix to the current docs:

1. Create a branch from the latest release tag, commit the fix and push it:

   ```shell
   git switch -c docs-fix v1.2.3
   git push -u origin docs-fix
   ```

2. Run the [`Docs`](workflows/docs.yml) workflow manually (**Actions → Docs → Run workflow**) on that branch.
3. Merge the fix into `main` too, so the next release keeps it.

Don't run the `Docs` workflow on `main`, because it would deploy the unreleased docs.

## If Something Goes Wrong

- **`build` job failed:** nothing was published. Fix on `main`, delete the tag (`git tag -d v1.2.3 && git push origin :refs/tags/v1.2.3`) and tag again.
- **`publish` job failed:** check the one-time setup, then **Re-run failed jobs**. Pushes use `--skip-duplicate` and an existing GitHub release is updated, so re-running is safe.
- **Published version is broken:** unlist it on nuget.org and release a new version. Version numbers can't be reused.
