#!/usr/bin/env dotnet
#:project ../../src/Baun/Baun.csproj

// A single-file baun build.
// It references the baun source code in this repository. A real build references the package instead:
// #:package Baun@0.1.0
//
// Run the entry step:  baun
// Run a step:          baun Test
// Without the tool:    dotnet run Build.cs -- Test
// On Linux and macOS:  ./Build.cs Test

await Baun.RunAsync<Build>();

[Entry(nameof(Test))]
partial class Build
{
    // Shared settings live in ordinary fields and properties.
    string Configuration = "Release";
    string ArtifactsDirectory = "artifacts";

    Step Clean() => _ => _
        .Executes(() =>
        {
            Console.WriteLine($"Deleting {ArtifactsDirectory}");
        });

    Step Restore() => _ => _
        .Executes(() =>
        {
            Console.WriteLine("dotnet restore");
        });

    Step Compile() => _ => _
        .DependsOn(Clean, Restore)
        .Executes(() =>
        {
            Console.WriteLine($"dotnet build --no-restore --configuration {Configuration}");
        });

    Step Test() => _ => _
        .DependsOn(Compile)
        .Executes(async () =>
        {
            Console.WriteLine($"dotnet test --no-build --configuration {Configuration}");
            await Task.CompletedTask;
        });

    Step Pack() => _ => _
        .DependsOn(Test)
        .Executes(() =>
        {
            Console.WriteLine($"dotnet pack --no-build --output {ArtifactsDirectory}");
        });
}
