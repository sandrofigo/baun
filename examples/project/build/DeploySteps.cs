// Deployment steps, kept separate from the main steps in Build.cs.
// This file is another part of the same partial class Build.

partial class Build
{
    Step DeployStaging() => _ => _
        .DependsOn(Pack)
        .Executes(async () =>
        {
            Console.WriteLine($"Uploading packages from {ArtifactsDirectory} to staging");
            await Task.CompletedTask;
        });

    Step DeployProduction() => _ => _
        .DependsOn(DeployStaging)
        .Executes(async () =>
        {
            Console.WriteLine($"Uploading packages from {ArtifactsDirectory} to production");
            await Task.CompletedTask;
        });
}
