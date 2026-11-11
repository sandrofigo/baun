// Deployment steps, kept separate from the main steps in Build.cs.
// This file is another part of the same partial class Build.

partial class Build
{
    Step DeployStaging() => _ => _
        .DependsOn(Pack)
        .Executes(async () =>
        {
            // upload the packages from {ArtifactsDirectory} to the staging environment
        });

    Step DeployProduction() => _ => _
        .DependsOn(DeployStaging)
        .Executes(async () =>
        {
            // upload the packages from {ArtifactsDirectory} to the production environment
        });
}
