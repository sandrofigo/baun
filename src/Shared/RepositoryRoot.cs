namespace Baun;

/// <summary>
/// Finds the root of the git repository a folder is in.
/// </summary>
internal static class RepositoryRoot
{
    /// <summary>
    /// The folder in the repository root that holds baun's files, such as <see cref="ParametersFileName"/>.
    /// </summary>
    public const string FolderName = ".baun";

    /// <summary>
    /// The file in <see cref="FolderName"/> that sets parameters of the build.
    /// </summary>
    public const string ParametersFileName = "parameters.json";

    /// <summary>
    /// Returns the first of <paramref name="directory"/> and its parents that contains <c>.git</c>, or
    /// <see langword="null"/> when <paramref name="directory"/> isn't in a git repository.
    /// </summary>
    /// <remarks>
    /// <c>.git</c> is a folder in a regular repository and a file in a worktree or submodule.
    /// </remarks>
    public static string? TryFind(string directory)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(directory)); current is not null; current = current.Parent)
        {
            string git = Path.Combine(current.FullName, ".git");

            if (Directory.Exists(git) || File.Exists(git))
            {
                return current.FullName;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the root of the git repository <paramref name="directory"/> is in, or <paramref name="directory"/>
    /// itself when it isn't in one.
    /// </summary>
    public static string Find(string directory) => TryFind(directory) ?? Path.GetFullPath(directory);

    /// <summary>
    /// Returns the path of the parameters file for builds run in <paramref name="directory"/>:
    /// <c>.baun/parameters.json</c> in the repository root.
    /// </summary>
    public static string GetParametersFile(string directory) =>
        Path.Combine(Find(directory), FolderName, ParametersFileName);
}
