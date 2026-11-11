#!/usr/bin/env dotnet
#:package Baun@0.1.0

// A single-file baun build.
//
// Run a step:          baun Test
// Without the tool:    dotnet run Build.cs -- Test
// On Linux and macOS:  ./Build.cs Test

await Baun.RunAsync<Build>(x => x.Test);

partial class Build
{
    // Shared settings live in ordinary fields and properties.
    string Configuration = "Release";
    string ArtifactsDirectory = "artifacts";

    Step Clean() => _ => _
        .Executes(() =>
        {
            // delete the output directories
            // e.g. Directory.Delete(ArtifactsDirectory, recursive: true);
        });

    Step Restore() => _ => _
        .Executes(() =>
        {
            // dotnet restore
        });

    Step Compile() => _ => _
        .DependsOn(Clean, Restore)
        .Executes(() =>
        {
            // dotnet build --no-restore --configuration {Configuration}
        });

    Step Test() => _ => _
        .DependsOn(Compile)
        .Executes(async () =>
        {
            // dotnet test --no-build --configuration {Configuration}
        });

    Step Pack() => _ => _
        .DependsOn(Test)
        .Executes(() =>
        {
            // dotnet pack --no-build --output {ArtifactsDirectory}
        });
}
