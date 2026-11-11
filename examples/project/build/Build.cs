// The entry point of a project-based baun build.
// The project file references baun with a PackageReference, so no #: lines are needed here.
//
// Run a step:          baun Test
// Without the tool:    dotnet run --project build -- Test
//
// The deployment steps live in DeploySteps.cs. Both files are parts of the same
// partial class, so steps can depend on each other across files without a prefix.

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
