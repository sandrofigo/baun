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
    /// The folder in <see cref="FolderName"/> for files baun creates while it runs, such as the plan page. It's ignored by
    /// git.
    /// </summary>
    public const string TempFolderName = "temp";

    /// <summary>
    /// Returns the first of <paramref name="directory"/> and its parents that contains a <c>.baun</c> folder, or
    /// <see langword="null"/> when there is none.
    /// </summary>
    public static AbsolutePath? TryFind(AbsolutePath directory)
    {
        for (AbsolutePath? current = directory; current is not null; current = current.Parent)
        {
            if (Directory.Exists(current / FolderName))
            {
                return current;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the path of <c>.baun/parameters.json</c> in the repository root of <paramref name="directory"/>, or
    /// <see langword="null"/> when no <c>.baun</c> folder is found.
    /// </summary>
    public static AbsolutePath? TryGetParametersFile(AbsolutePath directory) =>
        TryFind(directory) is { } root ? root / FolderName / ParametersFileName : null;
}
