using System.Diagnostics.CodeAnalysis;

namespace Baun.CI.GitHub;

/// <summary>
/// A GitHub repository, identified by its owner and name, such as <c>octocat/Hello-World</c>.
/// </summary>
/// <param name="Owner">The user or organization that owns the repository, such as <c>octocat</c>.</param>
/// <param name="Name">The name of the repository, such as <c>Hello-World</c>.</param>
public sealed record GitHubRepository(string Owner, string Name)
{
    /// <summary>
    /// Parses a repository in the form <c>owner/name</c>, such as <c>octocat/Hello-World</c>.
    /// </summary>
    /// <exception cref="FormatException"><paramref name="value"/> isn't in the form <c>owner/name</c>.</exception>
    public static GitHubRepository Parse(string value) =>
        TryParse(value, out GitHubRepository? repository)
            ? repository
            : throw new FormatException($"The repository '{value}' isn't in the form owner/name.");

    /// <summary>
    /// Tries to parse a repository in the form <c>owner/name</c>, such as <c>octocat/Hello-World</c>.
    /// </summary>
    /// <returns><see langword="true"/> if <paramref name="value"/> is in the form <c>owner/name</c>.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out GitHubRepository? repository)
    {
        repository = value?.Split('/') is [{ Length: > 0 } owner, { Length: > 0 } name]
            ? new GitHubRepository(owner, name)
            : null;

        return repository is not null;
    }

    /// <summary>
    /// Returns the repository in the form <c>owner/name</c>, such as <c>octocat/Hello-World</c>.
    /// </summary>
    public override string ToString() => $"{Owner}/{Name}";
}
