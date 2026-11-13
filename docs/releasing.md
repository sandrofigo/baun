# Releasing baun

This guide describes how to publish a new version of the `Baun` and `Baun.Cli` packages to nuget.org.

Publishing is done by the [`Release`](../.github/workflows/release.yml) workflow. It runs when a GitHub release is published, tests and packs both packages with the version from the release's tag, and pushes them to nuget.org with [trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing). No API key is stored in the repository.

> [!IMPORTANT]
> Versions published to nuget.org can't be deleted or replaced, only unlisted. Check everything before you publish a release, and never reuse a version number.

## One-time setup

These settings are already in place. Check them if publishing fails, or when you set up a fork.

- **Trusted publishing policy on nuget.org** (your username → Trusted Publishing):
  - Repository Owner: `sandrofigo`
  - Repository: `baun`
  - Workflow File: `release.yml`
  - Environment: `release`
- **Repository secret `NUGET_USER`** (Settings → Secrets and variables → Actions): the nuget.org username (profile name, not the email address) of the account that owns the policy.
- **Environment `release`** (Settings → Environments):
  - Deployment branches and tags: a **tag** rule for `v*`. A branch rule doesn't match, because the workflow runs on the release tag.
  - Optional: required reviewers, to approve each release before it is pushed to nuget.org.

## 1. Choose the version

baun uses [semantic versioning](https://semver.org). The release tag is the version with a `v` in front:

| Release | Tag | Package version |
| --- | --- | --- |
| Stable | `v1.2.3` | `1.2.3` |
| Prerelease | `v1.2.3-preview.1` | `1.2.3-preview.1` |

The workflow rejects any other tag format. The `<Version>` in [`Directory.Build.props`](../Directory.Build.props) is only used for local builds; the tag always decides the published version. You can update it to the released version to keep local builds in step, but it isn't required.

## 2. Mark the analyzer rules as shipped

The source generator reports its own compiler errors (`BAUN001`, `BAUN002`, ...). Their IDs are tracked in two files in [`src/Baun.SourceGenerators`](../src/Baun.SourceGenerators):

- `AnalyzerReleases.Unshipped.md`: rules added or changed since the last release.
- `AnalyzerReleases.Shipped.md`: rules per published release.

If `AnalyzerReleases.Unshipped.md` lists any rules, move them to `AnalyzerReleases.Shipped.md` under a heading for the new version, and leave only the header comments in the unshipped file.

For example, for version `0.1.0`, `AnalyzerReleases.Shipped.md` becomes:

```markdown
; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
BAUN001 | Baun | Error | StepGenerator
BAUN002 | Baun | Error | StepGenerator
BAUN004 | Baun | Error | StepGenerator
```

And `AnalyzerReleases.Unshipped.md` becomes:

```markdown
; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
```

Later releases add their own `## Release x.y.z` section below the earlier ones. Rules that were changed or removed go under `### Changed Rules` or `### Removed Rules` in that section, as described in the [release tracking documentation](https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md).

Build the solution afterwards. A rule that is missing from both files causes warning `RS2000`.

## 3. Prepare `main`

The workflow runs from the commit the release tag points to, so everything that should be released must be on `main`.

1. Merge the changes from steps 1 and 2 into `main` with a pull request.
2. Make sure the `Test` workflow passed on that pull request.
3. Optionally, check the release locally from an up-to-date `main`:

   ```shell
   dotnet test Baun.slnx --configuration Release
   dotnet pack Baun.slnx --configuration Release --output artifacts -p:Version=1.2.3
   ```

## 4. Publish the GitHub release

1. Open **Releases → Draft a new release** on GitHub.
2. **Choose a tag:** type the new tag, for example `v1.2.3`, and choose **Create new tag on publish**.
3. **Target:** `main`.
4. **Title:** the version, for example `v1.2.3`.
5. **Description:** click **Generate release notes** and edit them as needed.
6. For a prerelease version, check **Set as a pre-release**.
7. Click **Publish release**.

Publishing the release starts the `Release` workflow. Saving a draft doesn't.

## 5. Approve and follow the workflow

1. Open the **Actions** tab and select the `Release` run for the new tag.
2. The `build` job tests and packs both packages. If it fails, nothing has been published.
3. The `publish` job waits for approval if the `release` environment has required reviewers. Click **Review deployments** and approve.
4. The `publish` job logs in to nuget.org and pushes both packages.

## 6. Check the published packages

1. Both packages appear on nuget.org within a few minutes. Search results and installs can take a bit longer while nuget.org indexes them.
   - https://www.nuget.org/packages/Baun
   - https://www.nuget.org/packages/Baun.Cli
2. Try the tool and a new build:

   ```shell
   dotnet tool update --global Baun.Cli --version 1.2.3
   baun init --single-file
   baun
   ```

   `baun init` references the Baun package with the same version as the tool, so this checks both packages together.

## If something goes wrong

- **The `build` job failed:** nothing was published. Fix the problem on `main`, delete the GitHub release and its tag, and publish the release again.
- **The `publish` job failed before pushing,** for example at the nuget.org login: check the one-time setup above, then click **Re-run failed jobs**. The temporary API key is requested again on every run.
- **Only one package was pushed:** re-run the failed jobs. The push uses `--skip-duplicate`, so the package that was already pushed is skipped.
- **A published version is broken:** unlist it on nuget.org (package page → Manage package → Listing) and release a new version with the fix. The version number can't be used again.
