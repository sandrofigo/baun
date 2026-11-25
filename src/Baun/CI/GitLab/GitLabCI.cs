using static Baun.EnvironmentVariables;

namespace Baun.CI.GitLab;

/// <summary>
/// Information about the GitLab CI/CD job the build runs in, read from the
/// <see href="https://docs.gitlab.com/ci/variables/predefined_variables/">predefined variables</see> that GitLab CI/CD sets.
/// </summary>
/// <remarks>
/// Each property reads its environment variable when it's used, so setting the variable yourself works too.
/// </remarks>
public static class GitLabCI
{
    /// <summary>
    /// Gets whether the build runs in GitLab CI/CD: <c>GITLAB_CI</c> is <c>true</c>.
    /// </summary>
    public static bool IsRunning => IsTrue("GITLAB_CI");
}
