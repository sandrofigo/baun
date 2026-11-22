using System.Diagnostics.CodeAnalysis;

namespace Baun.CI.GitHub;

/// <summary>
/// A release of a GitHub repository. Get an existing one with <see cref="GitHubReleases.GetAsync"/>, or create a new one
/// such as <c>new GitHubRelease { Tag = "v1.2.3" }</c>, change it, then save it with
/// <see cref="GitHubReleases.CreateOrUpdateAsync"/>.
/// </summary>
/// <remarks>
/// Changing a release doesn't call GitHub. The changes, also the added and removed assets, are sent when the release is
/// saved.
/// </remarks>
public sealed class GitHubRelease
{
    private readonly List<AbsolutePath> addedAssets = [];
    private readonly List<GitHubReleaseAsset> removedAssets = [];

    /// <summary>
    /// Creates a new release. Set at least its <see cref="Tag"/>.
    /// </summary>
    public GitHubRelease()
    {
    }

    [SetsRequiredMembers]
    internal GitHubRelease(GitHubRepository repository, GitHubReleaseSnapshot snapshot)
    {
        Repository = repository;
        Apply(snapshot);
    }

    /// <summary>
    /// Gets the ID of the release, or <see langword="null"/> if it isn't saved yet.
    /// </summary>
    public long? Id { get; private set; }

    /// <summary>
    /// Gets or sets the tag of the release, such as <c>v1.2.3</c>. GitHub creates the tag, if it doesn't exist, when
    /// the release is published.
    /// </summary>
    public required string Tag { get; set; }

    /// <summary>
    /// Gets or sets the title of the release. If it's <see langword="null"/>, a new release is titled after its tag
    /// and an existing release keeps its title.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the release notes, in Markdown, such as the notes from <see cref="Changelog.GetReleaseNotes"/>. If
    /// they're <see langword="null"/>, an existing release keeps its notes.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets whether the release is a draft, which only people with write access to the repository see.
    /// Setting it to <see langword="false"/> publishes a draft when the release is saved.
    /// </summary>
    public bool Draft { get; set; }

    /// <summary>
    /// Gets or sets whether the release is marked as a prerelease.
    /// </summary>
    public bool Prerelease { get; set; }

    /// <summary>
    /// Gets or sets the repository of the release. If it's <see langword="null"/>, the release is saved to
    /// <see cref="GitHubActions.Repository"/>.
    /// </summary>
    public GitHubRepository? Repository { get; set; }

    /// <summary>
    /// Gets the URL of the release page on GitHub, or <see langword="null"/> if it isn't saved yet.
    /// </summary>
    public Uri? Url { get; private set; }

    /// <summary>
    /// Gets the files attached to the release on GitHub, without the changes that aren't saved yet.
    /// </summary>
    public IReadOnlyList<GitHubReleaseAsset> Assets { get; private set; } = [];

    /// <summary>
    /// Gets the files that are uploaded when the release is saved.
    /// </summary>
    public IReadOnlyList<AbsolutePath> AddedAssets => addedAssets;

    /// <summary>
    /// Gets the attached assets that are deleted when the release is saved.
    /// </summary>
    public IReadOnlyList<GitHubReleaseAsset> RemovedAssets => removedAssets;

    internal bool IsDraftOnGitHub { get; private set; }

    /// <summary>
    /// Adds a file to upload when the release is saved, named after the file. It replaces an attached asset with the
    /// same name.
    /// </summary>
    /// <param name="file">The file to attach.</param>
    /// <returns>This release.</returns>
    /// <exception cref="ArgumentException">An added file has the same name.</exception>
    public GitHubRelease AddAsset(AbsolutePath file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (addedAssets.FirstOrDefault(added => SameName(added.Name, file.Name)) is { } duplicate)
        {
            throw new ArgumentException($"An asset named {file.Name} is already added: {duplicate}.", nameof(file));
        }

        addedAssets.Add(file);
        return this;
    }

    /// <summary>
    /// Adds files to upload when the release is saved, each named after its file. They replace attached assets with
    /// the same names.
    /// </summary>
    /// <param name="files">The files to attach.</param>
    /// <returns>This release.</returns>
    /// <exception cref="ArgumentException">Two files have the same name.</exception>
    public GitHubRelease AddAssets(IEnumerable<AbsolutePath> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        foreach (AbsolutePath file in files)
        {
            AddAsset(file);
        }

        return this;
    }

    /// <summary>
    /// Removes the asset named <paramref name="name"/>: an added file isn't uploaded, and an attached asset is deleted
    /// when the release is saved.
    /// </summary>
    /// <param name="name">The file name of the asset, ignoring case.</param>
    /// <returns>Whether an asset with the name was added or attached.</returns>
    public bool RemoveAsset(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        bool removed = addedAssets.RemoveAll(added => SameName(added.Name, name)) > 0;

        if (Assets.FirstOrDefault(asset => SameName(asset.Name, name)) is { } attached && !removedAssets.Contains(attached))
        {
            removedAssets.Add(attached);
            removed = true;
        }

        return removed;
    }

    /// <inheritdoc/>
    public override string ToString() => Url is null ? Tag : $"{Name ?? Tag} ({Url})";

    internal static bool SameName(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    [MemberNotNull(nameof(Tag))]
    internal void Apply(GitHubReleaseSnapshot snapshot)
    {
        Id = snapshot.Id;
        Tag = snapshot.Tag;
        Name = snapshot.Name;
        Notes = snapshot.Notes;
        Draft = snapshot.Draft;
        IsDraftOnGitHub = snapshot.Draft;
        Prerelease = snapshot.Prerelease;
        Url = snapshot.Url;
        Assets = snapshot.Assets;
    }

    internal void ClearAssetChanges()
    {
        addedAssets.Clear();
        removedAssets.Clear();
    }

    // After deleting, the release is new again, so saving it creates it again.
    internal void Detach()
    {
        Id = null;
        Url = null;
        Assets = [];
        IsDraftOnGitHub = false;
        removedAssets.Clear();
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
