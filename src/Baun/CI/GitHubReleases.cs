namespace Baun.CI;

internal static class GitHubReleases
{
    /// <summary>
    /// Creates the release for the tag of <paramref name="data"/> in <paramref name="repository"/>, or updates it if
    /// it already exists, and uploads <paramref name="assets"/>, replacing assets with the same name.
    /// </summary>
    /// <remarks>
    /// A new release with assets that isn't a draft is created as a draft and published after the uploads, so it's
    /// never visible without its assets.
    /// </remarks>
    public static async Task<GitHubRelease> CreateOrUpdate(
        IGitHubApi api,
        GitHubRepository repository,
        GitHubReleaseData data,
        IReadOnlyList<AbsolutePath> assets,
        CancellationToken cancellationToken)
    {
        GitHubRelease? existing = await api.GetReleaseByTag(repository, data.Tag, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        bool publishAfterUpload = assets.Count > 0 && !data.Draft && (existing is null || existing.Draft);
        GitHubReleaseData first = publishAfterUpload ? data with { Draft = true } : data;

        GitHubRelease release = existing is null
            ? await api.CreateRelease(repository, first, cancellationToken)
            : await api.UpdateRelease(repository, existing.Id, first, cancellationToken);

        if (assets.Count == 0)
        {
            return release;
        }

        var names = assets.Select(asset => asset.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var uploaded = new List<GitHubReleaseAsset>(assets.Count);

        foreach (GitHubReleaseAsset old in release.Assets.Where(asset => names.Contains(asset.Name)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await api.DeleteAsset(repository, old.Id, cancellationToken);
        }

        foreach (AbsolutePath asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uploaded.Add(await api.UploadAsset(repository, release, asset, cancellationToken));
        }

        IReadOnlyList<GitHubReleaseAsset> allAssets = [.. release.Assets.Where(asset => !names.Contains(asset.Name)), .. uploaded];

        if (publishAfterUpload)
        {
            cancellationToken.ThrowIfCancellationRequested();
            release = await api.UpdateRelease(repository, release.Id, data, cancellationToken);
        }

        return release with { Assets = allAssets };
    }
}
