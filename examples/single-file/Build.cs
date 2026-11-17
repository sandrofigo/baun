#!/usr/bin/env dotnet
#:package Baun@0.6.0

// A single-file baun build.
//
// Run the entry step:  baun
// Run a step:          baun Test
// Set a parameter:     baun Publish --configuration Debug
// List steps:          baun --help
// Without the tool:    dotnet run Build.cs -- Test
// On Linux and macOS:  ./Build.cs Test

[Entry(nameof(Test))]
partial class Build
{
    // Parameters are set from the command line (--configuration Debug), environment variables (CONFIGURATION=Debug)
    // or .baun/parameters.json, in that order. Without a value, they keep their initial value.
    [Parameter("Configuration to build")]
    string Configuration = "Release";

    // RootDirectory is the folder that contains .baun, so paths don't depend on where the build runs from.
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    Step Clean() => _ => _
        .Executes(() =>
        {
            Log.Information("Deleting {ArtifactsDirectory}", ArtifactsDirectory);
        });

    Step Restore() => _ => _
        .Executes(() =>
        {
            Log.Information("dotnet restore");
        });

    Step Compile() => _ => _
        .DependsOn(Clean, Restore)
        .Executes(() =>
        {
            Log.Information("dotnet build --no-restore --configuration {Configuration}", Configuration);
        });

    Step Test() => _ => _
        .DependsOn(Compile)
        .Executes(async () =>
        {
            Log.Information("dotnet test --no-build --configuration {Configuration}", Configuration);
            await Task.CompletedTask;
        });

    Step Publish() => _ => _
        .DependsOn(Test)
        .Executes(() =>
        {
            Log.Information("dotnet publish --no-build --configuration {Configuration} --output {ArtifactsDirectory}", Configuration, ArtifactsDirectory);
        });
}
