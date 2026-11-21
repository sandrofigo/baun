---
title: Development
description: Build and test baun itself.
---

The source code is in the `src` folder:

- `src/Baun`: the library that builds reference.
- `src/Baun.SourceGenerators`: the source generator, shipped inside the Baun package.
- `src/Baun.Cli`: the baun CLI, published as the Baun.Cli package.

Build and test everything with the .NET 10 SDK. The tests use [xUnit.net v3](https://xunit.net) on Microsoft Testing Platform, which [`global.json`](https://github.com/sandrofigo/baun/blob/main/global.json) enables for `dotnet test`:

```shell
dotnet test --solution Baun.slnx
```

The documentation site is a [Starlight](https://starlight.astro.build) project in the `docs` folder. Preview it with Node.js:

```shell
cd docs
npm install
npm run dev
```

## Publishing

The [`Release`](https://github.com/sandrofigo/baun/blob/main/.github/workflows/release.yml) workflow publishes the Baun and Baun.Cli packages to nuget.org with [trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing), so no long-lived API key is stored in the repository. It runs when a version tag is pushed, and the tag sets the version: the tag v1.2.3 publishes version 1.2.3, and the build then creates the GitHub release. See [Releasing baun](https://github.com/sandrofigo/baun/blob/main/.github/RELEASING.md) for the steps, and [`CHANGELOG.md`](https://github.com/sandrofigo/baun/blob/main/CHANGELOG.md) for the changes in each version.
