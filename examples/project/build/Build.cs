// The entry point of a project-based baun build.
//
// Run the entry step:  baun
// Run a step:          baun Test
// Set a parameter:     baun Pack --configuration Debug --package-version 1.2.3
// List steps:          baun --help
// Without the tool:    dotnet run --project build -- Test

[Entry(nameof(Test))]
partial class Build
{
    // Parameters are set from the command line (--configuration Debug), environment variables (CONFIGURATION=Debug)
    // or .baun/parameters.json, in that order. Without a value, they keep their initial value.
    [Parameter("Configuration to build")]
    string Configuration = "Release";

    [Parameter("Version of the packages")]
    string PackageVersion = "1.0.0-dev";

    // RootDirectory is the folder that contains .baun, so paths don't depend on where the build runs from.
    string ArtifactsDirectory => Path.Combine(RootDirectory, "artifacts");

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
            Console.WriteLine($"dotnet pack --no-build --configuration {Configuration} --output {ArtifactsDirectory} -p:Version={PackageVersion}");
        });
}
