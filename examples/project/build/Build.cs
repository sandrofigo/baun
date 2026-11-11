// The entry point of a project-based baun build.
// Build.csproj references baun, so no #: lines are needed here.
//
// Run the entry step:  baun
// Run a step:          baun Test
// Without the tool:    dotnet run --project build -- Test
//
// The deployment steps live in DeploySteps.cs. Both files are parts of the same
// partial class, so steps can depend on each other across files without a prefix.

await Baun.RunAsync<Build>();

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

    [Entry]
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
