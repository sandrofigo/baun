---
title: Diagnostics
description: The errors and warnings baun's source generator reports when a build compiles.
---

baun's source generator checks the build class when it compiles and reports mistakes as compiler errors and warnings, so they show up in your IDE before the build runs. Each one has an ID that starts with `BAUN`.

| ID | Severity | Title |
| --- | --- | --- |
| [`BAUN001`](#baun001) | <span class="baun-severity-error">Error</span> | Build class must be partial |
| [`BAUN002`](#baun002) | <span class="baun-severity-error">Error</span> | Build class is not supported |
| [`BAUN004`](#baun004) | <span class="baun-severity-error">Error</span> | Entry is not a step |
| [`BAUN005`](#baun005) | <span class="baun-severity-error">Error</span> | More than one build class without an entry point |
| [`BAUN006`](#baun006) | <span class="baun-severity-error">Error</span> | Step names differ only in case |
| [`BAUN007`](#baun007) | <span class="baun-severity-warning">Warning</span> | Step name is a baun tool command |
| [`BAUN008`](#baun008) | <span class="baun-severity-error">Error</span> | Parameter must be writable |
| [`BAUN009`](#baun009) | <span class="baun-severity-error">Error</span> | Parameter type is not supported |
| [`BAUN010`](#baun010) | <span class="baun-severity-warning">Warning</span> | Parameter is not in a build class |
| [`BAUN011`](#baun011) | <span class="baun-severity-error">Error</span> | Parameter name conflicts |
| [`BAUN012`](#baun012) | <span class="baun-severity-warning">Warning</span> | Parameter hides a built-in option |

To turn off a warning you accept on purpose, add its ID to `<NoWarn>` in the project file of the build:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);BAUN012</NoWarn>
</PropertyGroup>
```

## BAUN001

The build class isn't `partial`. The source generator adds code to the build class, so it must be declared `partial class Build`.

## BAUN002

The build class is generic or nested in another type. Declare it as a non-generic class at the top level of a file.

## BAUN004

The entry attribute names something that isn't a step of the build class. Use nameof with the step method, such as `[Entry(nameof(Test))]`, so the compiler checks the name.

## BAUN005

The build has more than one class with steps and no entry point of its own, so baun can't choose which one to run. Write the entry point yourself and run one of them with `await BaunBuild.RunAsync<Build>();` (see [Custom Entry Point](../../guides/concept/#custom-entry-point)).

## BAUN006

Two steps have names that differ only in case, such as Test and test. Step names are case-insensitive on the command line, so rename one of them.

## BAUN007

A step has the same name as a command of the baun CLI, such as Init, so `baun Init` runs the command instead of the step. Run the step with `baun --step Init` (see [Steps Named Like Commands](../../reference/cli/#steps-named-like-commands)), or rename it.

## BAUN008

A parameter can't be set: it's a const or static field, a static property, or a property with no setter or only an init accessor. Make it an instance field, which may be readonly, or an instance property with a setter.

## BAUN009

A parameter has a type that baun can't set. See [Types](../../guides/parameters/#types) for the supported ones.

## BAUN010

A parameter is in a class without steps, so baun never sets it. Move it to the build class.

## BAUN011

A parameter's name conflicts with another name. Names match case-insensitively and ignore dashes and underscores, so NuGetApiKey and NUGET_API_KEY are the same name. The parameter conflicts with:

- another parameter of the same name,
- the built-in option `--help`, `--version`, `-h` or `-v`, or
- the prefix `--baun-` of the built-in options, because its name starts with Baun.

Rename it, or set another name with `[Parameter(Name = "...")]`.

## BAUN012

A parameter has the same name as the built-in option `--plan`, `--step` or `--verbosity`, so it takes over that option. The built-in option stays available with the prefix `--baun-`, such as `--baun-plan` (see [Built-in Options](../../guides/parameters/#built-in-options)). Set another name with `[Parameter(Name = "...")]` to keep the short option, or add BAUN012 to `<NoWarn>` if the parameter takes over the option on purpose.
