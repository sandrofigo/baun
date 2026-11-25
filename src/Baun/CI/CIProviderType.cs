namespace Baun.CI;

/// <summary>
/// A CI provider that baun detects, for example to write log groups. <see cref="CIProvider.Current"/> returns the one
/// the build runs on.
/// </summary>
public enum CIProviderType
{
    /// <summary>
    /// No supported CI provider, such as a local run.
    /// </summary>
    None,

    /// <summary>
    /// GitHub Actions: <see cref="GitHub.GitHubActions.IsRunning"/>.
    /// </summary>
    GitHubActions,

    /// <summary>
    /// Azure Pipelines: <see cref="AzureDevOps.AzurePipelines.IsRunning"/>.
    /// </summary>
    AzurePipelines,

    /// <summary>
    /// GitLab CI/CD: <see cref="GitLab.GitLabCI.IsRunning"/>.
    /// </summary>
    GitLab,

    /// <summary>
    /// TeamCity: <see cref="TeamCity.TeamCityBuild.IsRunning"/>.
    /// </summary>
    TeamCity,
}
