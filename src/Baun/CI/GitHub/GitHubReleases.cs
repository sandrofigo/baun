namespace Baun.CI.GitHub;

/// <summary>
/// Gets, saves and deletes the releases of a GitHub repository. Get an existing release with <see cref="GetAsync"/>,
/// or create a new one such as <c>new GitHubRelease { Tag = "v1.2.3" }</c>, change it, then save it with
/// <see cref="CreateOrUpdateAsync"/>.
/// </summary>
/// <remarks>
/// The repository defaults to <see cref="GitHubActions.Repository"/>, the token to <see cref="GitHubActions.Token"/>
/// and the API URL is <see cref="GitHubActions.ApiUrl"/>.
/// </remarks>
public static class GitHubReleases
{
    /// <summary>
    /// Gets the release for <paramref name="tag"/>, or <see langword="null"/> if there's none. Change the returned
    /// release, then save the changes with <see cref="CreateOrUpdateAsync"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// GitHubRelease? release = await GitHubReleases.GetAsync("v1.2.3");
    /// release!.Name = "Version 1.2.3";
    /// await GitHubReleases.CreateOrUpdateAsync(release);
    /// </code>
    /// </example>
    /// <param name="tag">The tag of the release, such as <c>v1.2.3</c>.</param>
    /// <param name="includeDrafts">Whether to also find a draft release for <paramref name="tag"/>.</param>
    /// <param name="repository">The repository of the release. Defaults to <see cref="GitHubActions.Repository"/>.</param>
    /// <param name="token">A token that may read releases of the repository. Defaults to <see cref="GitHubActions.Token"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The release, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="tag"/> or <paramref name="token"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="repository"/> isn't passed and <see cref="GitHubActions.Repository"/> is <see langword="null"/>,
    /// or <paramref name="token"/> isn't passed and <see cref="GitHubActions.Token"/> is <see langword="null"/>.
    /// </exception>
    public static Task<GitHubRelease?> GetAsync(
        string tag,
        bool includeDrafts = true,
        GitHubRepository? repository = null,
        string? token = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tag);

        return GetCoreAsync(CreateApi(token), ResolveRepository(repository), tag, includeDrafts, cancellationToken);
    }

    /// <summary>
    /// Saves <paramref name="release"/> to GitHub: creates it, or updates it if it already exists, also as a draft, so
    /// a step that calls it can be run again. Then uploads the added assets and deletes the removed ones.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A release from <see cref="GetAsync"/> or an earlier save is updated by its ID. A new release is matched to an
    /// existing one by its tag. The title and notes of an existing release are kept if they're
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
    /// await GitHubReleases.CreateOrUpdateAsync(release);
    /// </code>
    /// </example>
    /// <param name="release">The release to save.</param>
    /// <param name="token">
    /// A token that may write releases of the repository, such as <c>secrets.GITHUB_TOKEN</c> of a workflow with
    /// <c>contents: write</c>. Defaults to <see cref="GitHubActions.Token"/>.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><paramref name="release"/>, with the values from GitHub.</returns>
    /// <exception cref="ArgumentException">
    /// The tag of <paramref name="release"/> or <paramref name="token"/> is empty.
    /// </exception>
    /// <exception cref="FileNotFoundException">An added asset doesn't exist.</exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="GitHubRelease.Repository"/> and <see cref="GitHubActions.Repository"/> are <see langword="null"/>,
    /// or <paramref name="token"/> isn't passed and <see cref="GitHubActions.Token"/> is <see langword="null"/>.
    /// </exception>
    public static Task<GitHubRelease> CreateOrUpdateAsync(
        GitHubRelease release, string? token = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        return CreateOrUpdateCoreAsync(CreateApi(token), ResolveRepository(release.Repository), release, cancellationToken);
    }

    /// <summary>
    /// Deletes <paramref name="release"/> from GitHub and keeps its tag. Afterwards, the release is new again, so
    /// saving it creates it again.
    /// </summary>
    /// <param name="release">The release to delete, from <see cref="GetAsync"/> or <see cref="CreateOrUpdateAsync"/>.</param>
    /// <param name="token">A token that may write releases of the repository. Defaults to <see cref="GitHubActions.Token"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="ArgumentException"><paramref name="token"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="release"/> isn't on GitHub, or <paramref name="token"/> isn't passed and
    /// <see cref="GitHubActions.Token"/> is <see langword="null"/>.
    /// </exception>
    public static Task DeleteAsync(GitHubRelease release, string? token = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (release.Id is null)
        {
            throw new InvalidOperationException($"The release {release.Tag} isn't on GitHub.");
        }

        return DeleteCoreAsync(CreateApi(token), ResolveRepository(release.Repository), release, cancellationToken);
    }

    internal static async Task<GitHubRelease?> GetCoreAsync(
        IGitHubApi api, GitHubRepository repository, string tag, bool includeDrafts, CancellationToken cancellationToken) =>
        await api.GetReleaseByTag(repository, tag, includeDrafts, cancellationToken) is { } snapshot
            ? new GitHubRelease(repository, snapshot)
            : null;

    internal static async Task<GitHubRelease> CreateOrUpdateCoreAsync(
        IGitHubApi api, GitHubRepository repository, GitHubRelease release, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(release.Tag))
        {
            throw new ArgumentException("The release has no tag.", nameof(release));
        }

        foreach (AbsolutePath file in release.AddedAssets)
        {
            if (!file.FileExists)
            {
                throw new FileNotFoundException($"The asset {file} doesn't exist.", file);
            }
        }

        long? id = release.Id;
        bool? draftOnGitHub = id is null ? null : release.IsDraftOnGitHub;

        // A new release is matched to an existing one by its tag, so a step that saves it can be run again.
        if (id is null && await api.GetReleaseByTag(repository, release.Tag, includeDrafts: true, cancellationToken) is { } existing)
        {
            id = existing.Id;
            draftOnGitHub = existing.Draft;
        }

        cancellationToken.ThrowIfCancellationRequested();

        bool publishAfterUpload = release.AddedAssets.Count > 0 && !release.Draft && draftOnGitHub is null or true;
        bool draft = release.Draft || publishAfterUpload;

        GitHubReleaseSnapshot snapshot = id is null
            ? await api.CreateRelease(
                repository, new NewGitHubRelease(release.Tag, release.Name ?? release.Tag, release.Notes, draft, release.Prerelease), cancellationToken)
            : await api.UpdateRelease(
                repository, id.Value, new GitHubReleaseChanges(release.Tag, release.Name, release.Notes, draft, release.Prerelease), cancellationToken);

        List<GitHubReleaseAsset> assets = [.. snapshot.Assets];

        IEnumerable<GitHubReleaseAsset> deletes = assets.Where(asset =>
            release.RemovedAssets.Any(removed => removed.Id == asset.Id)
            || release.AddedAssets.Any(added => GitHubRelease.SameName(added.Name, asset.Name)));

        foreach (GitHubReleaseAsset asset in deletes.ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();

            await api.DeleteAsset(repository, asset.Id, cancellationToken);
            assets.Remove(asset);
        }

        foreach (AbsolutePath file in release.AddedAssets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            assets.Add(await api.UploadAsset(repository, snapshot.Id, file, cancellationToken));
        }

        if (publishAfterUpload)
        {
            cancellationToken.ThrowIfCancellationRequested();

            snapshot = await api.UpdateRelease(repository, snapshot.Id, new GitHubReleaseChanges(Draft: false), cancellationToken);
        }

        release.Repository = repository;
        release.Apply(snapshot with { Assets = assets });
        release.ClearAssetChanges();

        return release;
    }

    internal static async Task DeleteCoreAsync(
        IGitHubApi api, GitHubRepository repository, GitHubRelease release, CancellationToken cancellationToken)
    {
        await api.DeleteRelease(repository, release.Id!.Value, cancellationToken);
        release.Detach();
    }

    private static GitHubRepository ResolveRepository(GitHubRepository? repository) =>
        repository ?? GitHubActions.Repository
        ?? throw new InvalidOperationException("The repository isn't set and GITHUB_REPOSITORY isn't set to owner/name.");

    private static OctokitGitHubApi CreateApi(string? token)
    {
        if (token is not null)
        {
            ArgumentException.ThrowIfNullOrEmpty(token);
        }

        token ??= GitHubActions.Token
            ?? throw new InvalidOperationException("The token isn't passed and GITHUB_TOKEN isn't set. In a workflow, set it with env: GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}.");

        return new OctokitGitHubApi(token, GitHubActions.ApiUrl is { } url ? new Uri(url) : null);
    }
}
