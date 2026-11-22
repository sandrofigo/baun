namespace Baun.CI;

/// <summary>
/// Describes the GitHub release for a tag, which <see cref="GitHub.Release"/> creates. Apply it with
/// <see cref="CreateOrUpdateAsync"/>, or get the existing release with <see cref="GetAsync"/>.
/// </summary>
/// <remarks>
/// A builder doesn't change: each method returns a new builder, so one can be the base of others.
/// </remarks>
public sealed class GitHubReleaseBuilder
{
    private readonly string tag;
    private string? name;
    private string? notes;
    private bool? draft;
    private bool? prerelease;
    private IReadOnlyList<AbsolutePath> assets = [];
    private GitHubRepository? repository;
    private string? token;

    internal GitHubReleaseBuilder(string tag)
    {
        ArgumentException.ThrowIfNullOrEmpty(tag);
        this.tag = tag;
    }

    /// <summary>
    /// Gets the tag of the release, such as <c>v1.2.3</c>.
    /// </summary>
    public string Tag => tag;

    /// <summary>
    /// Sets the title of the release. A new release is titled after its tag by default.
    /// </summary>
    /// <param name="name">The title.</param>
    public GitHubReleaseBuilder Name(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return With(builder => builder.name = name);
    }

    /// <summary>
    /// Sets the release notes, in Markdown, such as the notes from <see cref="Changelog.GetReleaseNotes"/>.
    /// </summary>
    /// <param name="notes">The notes.</param>
    public GitHubReleaseBuilder Notes(string notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        return With(builder => builder.notes = notes);
    }

    /// <summary>
    /// Sets whether the release is a draft, which only people with write access to the repository see. Passing
    /// <see langword="false"/> for an existing draft publishes it. A new release isn't a draft by default.
    /// </summary>
    /// <param name="draft">Whether the release is a draft.</param>
    public GitHubReleaseBuilder Draft(bool draft = true) => With(builder => builder.draft = draft);

    /// <summary>
    /// Sets whether the release is marked as a prerelease. A new release isn't a prerelease by default.
    /// </summary>
    /// <param name="prerelease">Whether the release is a prerelease.</param>
    public GitHubReleaseBuilder Prerelease(bool prerelease = true) => With(builder => builder.prerelease = prerelease);

    /// <summary>
    /// Adds files to attach to the release, each named after its file.
    /// </summary>
    /// <param name="files">The files.</param>
    public GitHubReleaseBuilder Assets(params AbsolutePath[] files) => Assets((IEnumerable<AbsolutePath>)files);

    /// <inheritdoc cref="Assets(AbsolutePath[])"/>
    public GitHubReleaseBuilder Assets(IEnumerable<AbsolutePath> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        AbsolutePath[] added = [.. files];

        return With(builder => builder.assets = [.. builder.assets, .. added]);
    }

    /// <summary>
    /// Sets the repository of the release. Defaults to <see cref="GitHub.Repository"/>.
    /// </summary>
    /// <param name="repository">
    /// The repository, such as <c>new GitHubRepository("octocat", "Hello-World")</c> or
    /// <c>GitHubRepository.Parse("octocat/Hello-World")</c>.
    /// </param>
    public GitHubReleaseBuilder Repository(GitHubRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return With(builder => builder.repository = repository);
    }

    /// <summary>
    /// Sets the token for the GitHub API. Defaults to <see cref="GitHub.Token"/>.
    /// </summary>
    /// <param name="token">
    /// A token that may write releases of the repository, such as <c>secrets.GITHUB_TOKEN</c> of a workflow with
    /// <c>contents: write</c>.
    /// </param>
    public GitHubReleaseBuilder Token(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        return With(builder => builder.token = token);
    }

    /// <summary>
    /// Creates the release, or updates it if it already exists, also as a draft, so a step that calls it can be run
    /// again. Then uploads the assets and replaces attached assets with the same names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An existing release keeps the values that aren't set, so a draft stays a draft unless
    /// <see cref="Draft"/> is <see langword="false"/>. Attached assets that aren't uploaded again are kept.
    /// </para>
    /// <para>
    /// A release with assets that is to be published is kept a draft until the uploads are done, so it's never visible
    /// without its assets. GitHub creates the tag, if it doesn't exist, when the release is published.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The created or updated release.</returns>
    /// <exception cref="FileNotFoundException">An asset doesn't exist.</exception>
    /// <exception cref="ArgumentException">Two assets have the same name.</exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Repository"/> isn't set and <see cref="GitHub.Repository"/> is <see langword="null"/>, or
    /// <see cref="Token"/> isn't set and <see cref="GitHub.Token"/> is <see langword="null"/>.
    /// </exception>
    public Task<GitHubRelease> CreateOrUpdateAsync(CancellationToken cancellationToken = default) =>
        CreateOrUpdateCoreAsync(CreateApi(), ResolveRepository(), cancellationToken);

    /// <summary>
    /// Gets the release for the tag, or <see langword="null"/> if there's none. Only <see cref="Repository"/> and
    /// <see cref="Token"/> are used.
    /// </summary>
    /// <param name="includeDrafts">Whether to also find a draft release for the tag.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The release, or <see langword="null"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Repository"/> isn't set and <see cref="GitHub.Repository"/> is <see langword="null"/>, or
    /// <see cref="Token"/> isn't set and <see cref="GitHub.Token"/> is <see langword="null"/>.
    /// </exception>
    public Task<GitHubRelease?> GetAsync(bool includeDrafts = true, CancellationToken cancellationToken = default) =>
        GetCoreAsync(CreateApi(), ResolveRepository(), includeDrafts, cancellationToken);

    internal async Task<GitHubRelease?> GetCoreAsync(
        IGitHubApi api, GitHubRepository repository, bool includeDrafts, CancellationToken cancellationToken) =>
        await api.GetReleaseByTag(repository, tag, includeDrafts, cancellationToken) is { } snapshot
            ? new GitHubRelease(api, repository, snapshot)
            : null;

    internal async Task<GitHubRelease> CreateOrUpdateCoreAsync(IGitHubApi api, GitHubRepository repository, CancellationToken cancellationToken)
    {
        GitHubRelease.ValidateAssets(assets, "assets");

        GitHubReleaseSnapshot? existing = await api.GetReleaseByTag(repository, tag, includeDrafts: true, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        bool isDraft = draft ?? existing?.Draft ?? false;
        bool publishAfterUpload = assets.Count > 0 && !isDraft && (existing is null || existing.Draft);

        GitHubReleaseSnapshot snapshot = existing is null
            ? await api.CreateRelease(
                repository, new NewGitHubRelease(tag, name ?? tag, notes, isDraft || publishAfterUpload, prerelease ?? false), cancellationToken)
            : await api.UpdateRelease(
                repository, existing.Id, new GitHubReleaseChanges(name, notes, publishAfterUpload ? null : draft, prerelease), cancellationToken);

        var release = new GitHubRelease(api, repository, snapshot);

        if (assets.Count > 0)
        {
            await release.UploadAssetsAsync(assets, cancellationToken);
        }

        if (publishAfterUpload)
        {
            await release.PublishAsync(cancellationToken);
        }

        return release;
    }

    private GitHubRepository ResolveRepository() =>
        repository ?? GitHub.Repository
        ?? throw new InvalidOperationException("The repository isn't set and GITHUB_REPOSITORY isn't set to owner/name.");

    private OctokitGitHubApi CreateApi()
    {
        string resolvedToken = token ?? GitHub.Token
            ?? throw new InvalidOperationException("The token isn't set and GITHUB_TOKEN isn't set. In a workflow, set it with env: GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}.");

        return new OctokitGitHubApi(resolvedToken, GitHub.ApiUrl is { } url ? new Uri(url) : null);
    }

    private GitHubReleaseBuilder With(Action<GitHubReleaseBuilder> change)
    {
        var copy = (GitHubReleaseBuilder)MemberwiseClone();
        change(copy);
        return copy;
    }
}
