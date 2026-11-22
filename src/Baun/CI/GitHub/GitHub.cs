using static Baun.EnvironmentVariables;

namespace Baun.CI.GitHub;

/// <summary>
/// The type of the ref a GitHub Actions workflow runs for.
/// </summary>
public enum GitHubRefType
{
    /// <summary>
    /// A branch, such as <c>main</c>.
    /// </summary>
    Branch,

    /// <summary>
    /// A tag, such as <c>v1.2.3</c>.
    /// </summary>
    Tag,
}

/// <summary>
/// Information about the GitHub Actions workflow run the build runs in, read from the
/// <see href="https://docs.github.com/en/actions/reference/variables-reference#default-environment-variables">default environment variables</see>
/// that GitHub Actions sets.
/// </summary>
/// <remarks>
/// Each property reads its environment variable when it's used, so setting the variable yourself, for example to try a
/// release build locally, works too. A property is <see langword="null"/> when its variable isn't set or is empty, such
/// as outside GitHub Actions.
/// </remarks>
public static class GitHub
{
    /// <summary>
    /// Gets whether the build runs in GitHub Actions: <c>GITHUB_ACTIONS</c> is <c>true</c>.
    /// </summary>
    public static bool IsActions => IsTrue("GITHUB_ACTIONS");

    /// <summary>
    /// Gets the full ref the workflow runs for, such as <c>refs/heads/main</c> or <c>refs/tags/v1.2.3</c>, from <c>GITHUB_REF</c>.
    /// </summary>
    public static string? Ref => Get("GITHUB_REF");

    /// <summary>
    /// Gets the short name of the ref the workflow runs for, such as <c>main</c> or <c>v1.2.3</c>, from <c>GITHUB_REF_NAME</c>.
    /// For a pull request that isn't merged, it's <c>&lt;pr_number&gt;/merge</c>.
    /// </summary>
    public static string? RefName => Get("GITHUB_REF_NAME");

    /// <summary>
    /// Gets whether branch protections or rulesets are configured for the ref the workflow runs for: <c>GITHUB_REF_PROTECTED</c> is <c>true</c>.
    /// </summary>
    public static bool IsRefProtected => IsTrue("GITHUB_REF_PROTECTED");

    /// <summary>
    /// Gets the type of the ref the workflow runs for, from <c>GITHUB_REF_TYPE</c>, or <see langword="null"/> if it's
    /// not set or not <c>branch</c> or <c>tag</c>.
    /// </summary>
    public static GitHubRefType? RefType => Get("GITHUB_REF_TYPE")?.ToLowerInvariant() switch
    {
        "branch" => GitHubRefType.Branch,
        "tag" => GitHubRefType.Tag,
        _ => null,
    };

    /// <summary>
    /// Gets whether the workflow runs for a branch.
    /// </summary>
    public static bool IsBranch => RefType == GitHubRefType.Branch;

    /// <summary>
    /// Gets whether the workflow runs for a tag.
    /// </summary>
    public static bool IsTag => RefType == GitHubRefType.Tag;

    /// <summary>
    /// Gets the tag the workflow runs for, such as <c>v1.2.3</c>, or <see langword="null"/> if it doesn't run for a tag.
    /// </summary>
    public static string? Tag => IsTag ? RefName : null;

    /// <summary>
    /// Gets the branch of the pull request the workflow runs for, from <c>GITHUB_HEAD_REF</c>. Only set for pull request events.
    /// </summary>
    public static string? HeadRef => Get("GITHUB_HEAD_REF");

    /// <summary>
    /// Gets the target branch of the pull request the workflow runs for, from <c>GITHUB_BASE_REF</c>. Only set for pull
    /// request events.
    /// </summary>
    public static string? BaseRef => Get("GITHUB_BASE_REF");

    /// <summary>
    /// Gets the commit SHA that triggered the workflow, from <c>GITHUB_SHA</c>.
    /// </summary>
    public static string? Sha => Get("GITHUB_SHA");

    /// <summary>
    /// Gets the repository, such as <c>octocat/Hello-World</c>, from <c>GITHUB_REPOSITORY</c>, or <see langword="null"/>
    /// if it's not set or not in the form <c>owner/name</c>.
    /// </summary>
    public static GitHubRepository? Repository => GitHubRepository.TryParse(Get("GITHUB_REPOSITORY"), out GitHubRepository? repository) ? repository : null;

    /// <summary>
    /// Gets the ID of the repository, from <c>GITHUB_REPOSITORY_ID</c>.
    /// </summary>
    public static long? RepositoryId => GetNumber("GITHUB_REPOSITORY_ID");

    /// <summary>
    /// Gets the owner of the repository, such as <c>octocat</c>, from <c>GITHUB_REPOSITORY_OWNER</c>.
    /// </summary>
    public static string? RepositoryOwner => Get("GITHUB_REPOSITORY_OWNER");

    /// <summary>
    /// Gets the account ID of the owner of the repository, from <c>GITHUB_REPOSITORY_OWNER_ID</c>.
    /// </summary>
    public static long? RepositoryOwnerId => GetNumber("GITHUB_REPOSITORY_OWNER_ID");

    /// <summary>
    /// Gets the name of the event that triggered the workflow, such as <c>push</c> or <c>workflow_dispatch</c>, from <c>GITHUB_EVENT_NAME</c>.
    /// </summary>
    public static string? EventName => Get("GITHUB_EVENT_NAME");

    /// <summary>
    /// Gets the path of the file on the runner with the full webhook payload of the event, from <c>GITHUB_EVENT_PATH</c>.
    /// </summary>
    public static AbsolutePath? EventPath => GetPath("GITHUB_EVENT_PATH");

    /// <summary>
    /// Gets the name of the workflow, from <c>GITHUB_WORKFLOW</c>. If the workflow file doesn't name the workflow, it's
    /// the path of the workflow file in the repository.
    /// </summary>
    public static string? Workflow => Get("GITHUB_WORKFLOW");

    /// <summary>
    /// Gets the ref path of the workflow, such as <c>octocat/hello-world/.github/workflows/my-workflow.yml@refs/heads/my_branch</c>,
    /// from <c>GITHUB_WORKFLOW_REF</c>.
    /// </summary>
    public static string? WorkflowRef => Get("GITHUB_WORKFLOW_REF");

    /// <summary>
    /// Gets the commit SHA of the workflow file, from <c>GITHUB_WORKFLOW_SHA</c>.
    /// </summary>
    public static string? WorkflowSha => Get("GITHUB_WORKFLOW_SHA");

    /// <summary>
    /// Gets the ID of the current job, from <c>GITHUB_JOB</c>.
    /// </summary>
    public static string? Job => Get("GITHUB_JOB");

    /// <summary>
    /// Gets the name of the person or app that started the workflow, from <c>GITHUB_ACTOR</c>.
    /// </summary>
    public static string? Actor => Get("GITHUB_ACTOR");

    /// <summary>
    /// Gets the account ID of the person or app that started the first run of the workflow, from <c>GITHUB_ACTOR_ID</c>.
    /// </summary>
    public static long? ActorId => GetNumber("GITHUB_ACTOR_ID");

    /// <summary>
    /// Gets the name of the user that started this run, from <c>GITHUB_TRIGGERING_ACTOR</c>. For a re-run, it can differ
    /// from <see cref="Actor"/>.
    /// </summary>
    public static string? TriggeringActor => Get("GITHUB_TRIGGERING_ACTOR");

    /// <summary>
    /// Gets the URL of the GitHub server, such as <c>https://github.com</c>, from <c>GITHUB_SERVER_URL</c>.
    /// </summary>
    public static string? ServerUrl => Get("GITHUB_SERVER_URL");

    /// <summary>
    /// Gets the URL of the REST API, such as <c>https://api.github.com</c>, from <c>GITHUB_API_URL</c>.
    /// </summary>
    public static string? ApiUrl => Get("GITHUB_API_URL");

    /// <summary>
    /// Gets the URL of the GraphQL API, such as <c>https://api.github.com/graphql</c>, from <c>GITHUB_GRAPHQL_URL</c>.
    /// </summary>
    public static string? GraphQLUrl => Get("GITHUB_GRAPHQL_URL");

    /// <summary>
    /// Gets the default working directory of the job on the runner, from <c>GITHUB_WORKSPACE</c>.
    /// </summary>
    public static AbsolutePath? Workspace => GetPath("GITHUB_WORKSPACE");

    /// <summary>
    /// Gets the path of the file on the runner that holds the job summary of the current step, from <c>GITHUB_STEP_SUMMARY</c>.
    /// Markdown appended to it is shown on the summary page of the run.
    /// </summary>
    public static AbsolutePath? StepSummary => GetPath("GITHUB_STEP_SUMMARY");

    /// <summary>
    /// Gets the number of days the logs and artifacts of the run are kept, from <c>GITHUB_RETENTION_DAYS</c>.
    /// </summary>
    public static long? RetentionDays => GetNumber("GITHUB_RETENTION_DAYS");

    /// <summary>
    /// Gets the unique number of the workflow run, from <c>GITHUB_RUN_ID</c>.
    /// </summary>
    public static long? RunId => GetNumber("GITHUB_RUN_ID");

    /// <summary>
    /// Gets the number of the workflow run, counted per workflow from 1, from <c>GITHUB_RUN_NUMBER</c>.
    /// </summary>
    public static long? RunNumber => GetNumber("GITHUB_RUN_NUMBER");

    /// <summary>
    /// Gets the attempt of the workflow run, starting at 1 and counting re-runs, from <c>GITHUB_RUN_ATTEMPT</c>.
    /// </summary>
    public static long? RunAttempt => GetNumber("GITHUB_RUN_ATTEMPT");

    /// <summary>
    /// Gets the operating system of the runner: <c>Linux</c>, <c>Windows</c> or <c>macOS</c>, from <c>RUNNER_OS</c>.
    /// </summary>
    public static string? RunnerOs => Get("RUNNER_OS");

    /// <summary>
    /// Gets the architecture of the runner: <c>X86</c>, <c>X64</c>, <c>ARM</c> or <c>ARM64</c>, from <c>RUNNER_ARCH</c>.
    /// </summary>
    public static string? RunnerArch => Get("RUNNER_ARCH");

    /// <summary>
    /// Gets the name of the runner, from <c>RUNNER_NAME</c>. It isn't always unique within a run.
    /// </summary>
    public static string? RunnerName => Get("RUNNER_NAME");

    /// <summary>
    /// Gets whether the runner is <c>github-hosted</c> or <c>self-hosted</c>, from <c>RUNNER_ENVIRONMENT</c>.
    /// </summary>
    public static string? RunnerEnvironment => Get("RUNNER_ENVIRONMENT");

    /// <summary>
    /// Gets the temporary directory of the runner, which is emptied at the beginning and end of each job, from <c>RUNNER_TEMP</c>.
    /// </summary>
    public static AbsolutePath? RunnerTemp => GetPath("RUNNER_TEMP");

    /// <summary>
    /// Gets the directory with the preinstalled tools of GitHub-hosted runners, from <c>RUNNER_TOOL_CACHE</c>.
    /// </summary>
    public static AbsolutePath? RunnerToolCache => GetPath("RUNNER_TOOL_CACHE");

    /// <summary>
    /// Gets whether debug logging is enabled for the run: <c>RUNNER_DEBUG</c> is <c>1</c>.
    /// </summary>
    public static bool IsDebug => Get("RUNNER_DEBUG") == "1";

    /// <summary>
    /// Gets the token for the GitHub API, from <c>GITHUB_TOKEN</c>.
    /// </summary>
    /// <remarks>
    /// GitHub Actions doesn't set this variable by itself. Pass the token of the run to the step that runs the build
    /// with <c>env: GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}</c>.
    /// </remarks>
    public static string? Token => Get("GITHUB_TOKEN");

    /// <summary>
    /// Gets the GitHub release for <paramref name="tag"/>, or <see langword="null"/> if there's none. Change the
    /// returned release, then save the changes with <see cref="CreateOrUpdateReleaseAsync"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// GitHubRelease? release = await GitHub.GetReleaseAsync("v1.2.3");
    /// release!.Name = "Version 1.2.3";
    /// await GitHub.CreateOrUpdateReleaseAsync(release);
    /// </code>
    /// </example>
    /// <param name="tag">The tag of the release, such as <c>v1.2.3</c>.</param>
    /// <param name="includeDrafts">Whether to also find a draft release for <paramref name="tag"/>.</param>
    /// <param name="repository">The repository of the release. Defaults to <see cref="Repository"/>.</param>
    /// <param name="token">A token that may read releases of the repository. Defaults to <see cref="Token"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The release, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="tag"/> or <paramref name="token"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="repository"/> isn't passed and <see cref="Repository"/> is <see langword="null"/>, or
    /// <paramref name="token"/> isn't passed and <see cref="Token"/> is <see langword="null"/>.
    /// </exception>
    public static Task<GitHubRelease?> GetReleaseAsync(
        string tag,
        bool includeDrafts = true,
        GitHubRepository? repository = null,
        string? token = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tag);

        return GitHubReleases.Get(CreateApi(token), ResolveRepository(repository), tag, includeDrafts, cancellationToken);
    }

    /// <summary>
    /// Saves <paramref name="release"/> to GitHub: creates it, or updates it if it already exists, also as a draft, so
    /// a step that calls it can be run again. Then uploads the added assets and deletes the removed ones.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A release from <see cref="GetReleaseAsync"/> or an earlier save is updated by its ID. A new release is matched
    /// to an existing one by its tag. The title and notes of an existing release are kept if they're
    /// <see langword="null"/>, and the draft and prerelease flags are set as they are on <paramref name="release"/>.
    /// </para>
    /// <para>
    /// An added asset replaces an attached asset with the same name. A release with added assets that is to be
    /// published is kept a draft until the uploads are done, so it's never visible without them. GitHub creates the
    /// tag, if it doesn't exist, when the release is published.
    /// </para>
    /// <para>
    /// Afterwards, <paramref name="release"/> has the values from GitHub, such as its ID, URL and assets.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var release = new GitHubRelease { Tag = "v1.2.3", Notes = notes };
    /// release.AddAsset(RootDirectory / "artifacts" / "app.zip");
    /// await GitHub.CreateOrUpdateReleaseAsync(release);
    /// </code>
    /// </example>
    /// <param name="release">The release to save.</param>
    /// <param name="token">
    /// A token that may write releases of the repository, such as <c>secrets.GITHUB_TOKEN</c> of a workflow with
    /// <c>contents: write</c>. Defaults to <see cref="Token"/>.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><paramref name="release"/>, with the values from GitHub.</returns>
    /// <exception cref="ArgumentException">
    /// The tag of <paramref name="release"/> or <paramref name="token"/> is empty.
    /// </exception>
    /// <exception cref="FileNotFoundException">An added asset doesn't exist.</exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="GitHubRelease.Repository"/> and <see cref="Repository"/> are <see langword="null"/>, or
    /// <paramref name="token"/> isn't passed and <see cref="Token"/> is <see langword="null"/>.
    /// </exception>
    public static Task<GitHubRelease> CreateOrUpdateReleaseAsync(
        GitHubRelease release, string? token = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        return GitHubReleases.CreateOrUpdate(CreateApi(token), ResolveRepository(release.Repository), release, cancellationToken);
    }

    /// <summary>
    /// Deletes <paramref name="release"/> from GitHub and keeps its tag.
    /// </summary>
    /// <param name="release">The release to delete, from <see cref="GetReleaseAsync"/> or <see cref="CreateOrUpdateReleaseAsync"/>.</param>
    /// <param name="token">A token that may write releases of the repository. Defaults to <see cref="Token"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="ArgumentException"><paramref name="token"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="release"/> isn't on GitHub, or <paramref name="token"/> isn't passed and <see cref="Token"/>
    /// is <see langword="null"/>.
    /// </exception>
    public static Task DeleteReleaseAsync(GitHubRelease release, string? token = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (release.Id is null)
        {
            throw new InvalidOperationException($"The release {release.Tag} isn't on GitHub.");
        }

        return GitHubReleases.Delete(CreateApi(token), ResolveRepository(release.Repository), release, cancellationToken);
    }

    private static GitHubRepository ResolveRepository(GitHubRepository? repository) =>
        repository ?? Repository
        ?? throw new InvalidOperationException("The repository isn't set and GITHUB_REPOSITORY isn't set to owner/name.");

    private static OctokitGitHubApi CreateApi(string? token)
    {
        if (token is not null)
        {
            ArgumentException.ThrowIfNullOrEmpty(token);
        }

        token ??= Token
            ?? throw new InvalidOperationException("The token isn't passed and GITHUB_TOKEN isn't set. In a workflow, set it with env: GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}.");

        return new OctokitGitHubApi(token, ApiUrl is { } url ? new Uri(url) : null);
    }
}
