using Baun.CI.AzureDevOps;
using Baun.CI.GitHub;
using Baun.CI.GitLab;
using Baun.CI.TeamCity;
using static Baun.EnvironmentVariables;

namespace Baun.CI;

/// <summary>
/// Information about the CI provider the build runs on, read from the environment variables each CI provider sets.
/// </summary>
/// <remarks>
/// Each property reads the environment variables when it's used, so setting a variable yourself, for example to try a
/// CI run locally, works too.
/// </remarks>
public static class CIProvider
{
    /// <summary>
    /// Gets the supported CI provider the build runs on, or <see cref="CIProviderType.None"/> outside CI or on a CI
    /// provider that baun doesn't support.
    /// </summary>
    public static CIProviderType Current
    {
        get
        {
            if (GitHubActions.IsRunning)
            {
                return CIProviderType.GitHubActions;
            }

            if (AzurePipelines.IsRunning)
            {
                return CIProviderType.AzurePipelines;
            }

            if (GitLabCI.IsRunning)
            {
                return CIProviderType.GitLab;
            }

            if (TeamCityBuild.IsRunning)
            {
                return CIProviderType.TeamCity;
            }

            return CIProviderType.None;
        }
    }

    /// <summary>
    /// Gets whether the build runs on a CI provider: <see cref="Current"/> isn't <see cref="CIProviderType.None"/>, or
    /// <c>CI</c> is <c>true</c>, which most other CI providers set.
    /// </summary>
    public static bool IsRunning => Current != CIProviderType.None || IsTrue("CI");
}
