namespace Baun;

/// <summary>
/// Finds the root of a baun repository: the folder that contains the <see cref="FolderName"/> folder.
/// </summary>
internal static class RepositoryRoot
{
    /// <summary>
    /// The folder that marks the repository root and holds baun's files, such as <see cref="ParametersFileName"/>.
    /// </summary>
    public const string FolderName = ".baun";

    /// <summary>
    /// The file in <see cref="FolderName"/> that sets parameters of the build.
    /// </summary>
    public const string ParametersFileName = "parameters.json";

    /// <summary>
    /// Returns the first of <paramref name="directory"/> and its parents that contains a <c>.baun</c> folder, or
    /// <see langword="null"/> when there is none.
    /// </summary>
    public static string? TryFind(string directory)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(directory)); current is not null; current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, FolderName)))
            {
                return current.FullName;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the path of <c>.baun/parameters.json</c> in the repository root of <paramref name="directory"/>, or
    /// <see langword="null"/> when no <c>.baun</c> folder is found.
    /// </summary>
    public static string? TryGetParametersFile(string directory) =>
        TryFind(directory) is { } root ? Path.Combine(root, FolderName, ParametersFileName) : null;
}
