using static Baun.EnvironmentVariables;

namespace Baun.CI.AzureDevOps;

/// <summary>
/// Information about the Azure Pipelines pipeline run the build runs in, read from the
/// <see href="https://learn.microsoft.com/en-us/azure/devops/pipelines/build/variables">predefined variables</see> that Azure Pipelines sets.
/// </summary>
/// <remarks>
/// Each property reads its environment variable when it's used, so setting the variable yourself works too.
/// </remarks>
public static class AzurePipelines
{
    /// <summary>
    /// Gets whether the build runs in Azure Pipelines: <c>TF_BUILD</c> is <c>True</c>.
    /// </summary>
    public static bool IsRunning => IsTrue("TF_BUILD");
}
