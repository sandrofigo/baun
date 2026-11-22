using Baun.CI.GitHub;
using static Baun.EnvironmentVariables;

namespace Baun.CI;

/// <summary>
/// A CI server whose log baun can structure, for example with log groups.
/// </summary>
internal enum CIProvider
{
    /// <summary>
    /// No supported CI server, such as a local run.
    /// </summary>
    None,

    /// <summary>
    /// GitHub Actions: <c>GITHUB_ACTIONS</c> is <c>true</c>.
    /// </summary>
    GitHubActions,

    /// <summary>
    /// Azure Pipelines: <c>TF_BUILD</c> is <c>True</c>.
    /// </summary>
    AzurePipelines,

    /// <summary>
    /// GitLab CI/CD: <c>GITLAB_CI</c> is <c>true</c>.
    /// </summary>
    GitLab,

    /// <summary>
    /// TeamCity: <c>TEAMCITY_VERSION</c> is set.
    /// </summary>
    TeamCity,
}

/// <summary>
/// Detects the CI server the build runs on.
/// </summary>
internal static class CIProviders
{
    /// <summary>
    /// Returns the CI server the build runs on, from the environment variables each server sets.
    /// </summary>
    public static CIProvider Detect()
    {
        if (GitHubActions.IsRunning)
        {
            return CIProvider.GitHubActions;
        }

        if (IsTrue("TF_BUILD"))
        {
            return CIProvider.AzurePipelines;
        }

        if (IsTrue("GITLAB_CI"))
        {
            return CIProvider.GitLab;
        }

        if (Get("TEAMCITY_VERSION") is not null)
        {
            return CIProvider.TeamCity;
        }

        return CIProvider.None;
    }
}
