namespace Baun.CI;

/// <summary>
/// A release of a GitHub repository, which <see cref="GitHubReleaseBuilder.CreateOrUpdateAsync"/> and
/// <see cref="GitHubReleaseBuilder.GetAsync"/> return.
/// </summary>
/// <remarks>
/// The methods change the release on GitHub right away, and the properties follow the changes.
/// </remarks>
public sealed class GitHubRelease
{
    private readonly IGitHubApi api;
    private bool deleted;

    internal GitHubRelease(IGitHubApi api, GitHubRepository repository, GitHubReleaseSnapshot snapshot)
    {
        this.api = api;
        Repository = repository;
        Apply(snapshot);
    }

    /// <summary>
    /// Gets the repository of the release.
    /// </summary>
    public GitHubRepository Repository { get; }

    /// <summary>
    /// Gets the ID of the release.
    /// </summary>
    public long Id { get; private set; }

    /// <summary>
    /// Gets the tag of the release, such as <c>v1.2.3</c>.
    /// </summary>
    public string Tag { get; private set; } = "";

    /// <summary>
    /// Gets the title of the release.
    /// </summary>
    public string Name { get; private set; } = "";

    /// <summary>
    /// Gets the release notes, in Markdown.
    /// </summary>
    public string? Notes { get; private set; }

    /// <summary>
    /// Gets whether the release is a draft, which only people with write access to the repository see.
    /// </summary>
    public bool Draft { get; private set; }

    /// <summary>
    /// Gets whether the release is marked as a prerelease.
    /// </summary>
    public bool Prerelease { get; private set; }

    /// <summary>
    /// Gets the URL of the release page on GitHub.
    /// </summary>
    public Uri Url { get; private set; } = null!;

    /// <summary>
    /// Gets the files attached to the release.
    /// </summary>
    public IReadOnlyList<GitHubReleaseAsset> Assets { get; private set; } = [];

    /// <summary>
    /// Uploads <paramref name="file"/> as an asset named after the file, and replaces an attached asset with the same name.
    /// </summary>
    /// <param name="file">The file to attach.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The uploaded asset.</returns>
    /// <exception cref="FileNotFoundException"><paramref name="file"/> doesn't exist.</exception>
    /// <exception cref="InvalidOperationException">The release is deleted.</exception>
    public async Task<GitHubReleaseAsset> UploadAssetAsync(AbsolutePath file, CancellationToken cancellationToken = default) =>
        (await UploadAssetsAsync([file], cancellationToken))[0];

    /// <summary>
    /// Uploads <paramref name="files"/> as assets named after each file, and replaces attached assets with the same
    /// names. Other attached assets are kept.
    /// </summary>
    /// <param name="files">The files to attach.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The uploaded assets, in the order of <paramref name="files"/>.</returns>
    /// <exception cref="FileNotFoundException">A file doesn't exist.</exception>
    /// <exception cref="ArgumentException">Two files have the same name.</exception>
    /// <exception cref="InvalidOperationException">The release is deleted.</exception>
    public async Task<IReadOnlyList<GitHubReleaseAsset>> UploadAssetsAsync(
        IEnumerable<AbsolutePath> files, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ThrowIfDeleted();

        AbsolutePath[] uploads = [.. files];
        ValidateAssets(uploads, nameof(files));

        var uploaded = new List<GitHubReleaseAsset>(uploads.Length);

        foreach (AbsolutePath file in uploads)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await DeleteAssetAsync(file.Name, cancellationToken);

            GitHubReleaseAsset asset = await api.UploadAsset(Repository, Id, file, cancellationToken);
            Assets = [.. Assets, asset];
            uploaded.Add(asset);
        }

        return uploaded;
    }

    /// <summary>
    /// Deletes the attached asset named <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The file name of the asset, ignoring case.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Whether an asset with the name was attached.</returns>
    /// <exception cref="InvalidOperationException">The release is deleted.</exception>
    public async Task<bool> DeleteAssetAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ThrowIfDeleted();

        if (Assets.FirstOrDefault(asset => string.Equals(asset.Name, name, StringComparison.OrdinalIgnoreCase)) is not { } existing)
        {
            return false;
        }

        await api.DeleteAsset(Repository, existing.Id, cancellationToken);
        Assets = [.. Assets.Where(asset => asset.Id != existing.Id)];

        return true;
    }

    /// <summary>
    /// Publishes the release if it's a draft. GitHub creates the tag if it doesn't exist.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The release is deleted.</exception>
    public async Task PublishAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();

        if (Draft)
        {
            Apply(await api.UpdateRelease(Repository, Id, new GitHubReleaseChanges(Draft: false), cancellationToken));
        }
    }

    /// <summary>
    /// Deletes the release from GitHub and keeps its tag. The release can't be used afterwards.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The release is already deleted.</exception>
    public async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDeleted();

        await api.DeleteRelease(Repository, Id, cancellationToken);
        deleted = true;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} ({Url})";

    internal static void ValidateAssets(IReadOnlyCollection<AbsolutePath> files, string parameterName)
    {
        foreach (AbsolutePath file in files)
        {
            ArgumentNullException.ThrowIfNull(file, parameterName);

            if (!file.FileExists)
            {
                throw new FileNotFoundException($"The asset {file} doesn't exist.", file);
            }
        }

        if (files.GroupBy(file => file.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1) is { } duplicate)
        {
            throw new ArgumentException($"More than one asset is named {duplicate.Key}: {string.Join(", ", duplicate)}.", parameterName);
        }
    }

    private void Apply(GitHubReleaseSnapshot snapshot)
    {
        Id = snapshot.Id;
        Tag = snapshot.Tag;
        Name = snapshot.Name;
        Notes = snapshot.Notes;
        Draft = snapshot.Draft;
        Prerelease = snapshot.Prerelease;
        Url = snapshot.Url;
        Assets = snapshot.Assets;
    }

    private void ThrowIfDeleted()
    {
        if (deleted)
        {
            throw new InvalidOperationException($"The release {Tag} is deleted.");
        }
    }
}

/// <summary>
/// A file attached to a GitHub release.
/// </summary>
/// <param name="Id">The ID of the asset.</param>
/// <param name="Name">The file name of the asset.</param>
/// <param name="Size">The size of the asset, in bytes.</param>
/// <param name="DownloadUrl">The URL to download the asset from.</param>
public sealed record GitHubReleaseAsset(long Id, string Name, long Size, Uri DownloadUrl);
