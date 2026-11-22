namespace Baun.CI;

/// <summary>
/// Gets, saves and deletes <see cref="GitHubRelease"/>s with an <see cref="IGitHubApi"/>, for <see cref="GitHub"/>.
/// </summary>
internal static class GitHubReleases
{
    public static async Task<GitHubRelease?> Get(
        IGitHubApi api, GitHubRepository repository, string tag, bool includeDrafts, CancellationToken cancellationToken) =>
        await api.GetReleaseByTag(repository, tag, includeDrafts, cancellationToken) is { } snapshot
            ? new GitHubRelease(repository, snapshot)
            : null;

    public static async Task<GitHubRelease> CreateOrUpdate(
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

    public static async Task Delete(IGitHubApi api, GitHubRepository repository, GitHubRelease release, CancellationToken cancellationToken)
    {
        await api.DeleteRelease(repository, release.Id!.Value, cancellationToken);
        release.Detach();
    }
}
