namespace Baun.Cli;

/// <summary>
/// A baun build found on disk.
/// </summary>
/// <param name="BuildFile">The path of the build's <c>Build.cs</c> file.</param>
/// <param name="ProjectFile">The path of the build project, or <see langword="null"/> for a single-file build.</param>
internal sealed record BuildLocation(AbsolutePath BuildFile, AbsolutePath? ProjectFile);

internal static class BuildLocator
{
    /// <summary>
    /// The file every build has, next to the build project for a project build.
    /// </summary>
    internal const string BuildFileName = "Build.cs";

    /// <summary>
    /// The subfolder of the repository that holds a project build.
    /// </summary>
    internal const string BuildFolderName = "build";

    /// <summary>
    /// The name of the build project that <c>baun init</c> creates.
    /// </summary>
    internal const string ProjectFileName = "_build.csproj";

    private static readonly string[] _searchFolders = [".", BuildFolderName];

    /// <summary>
    /// Looks for a build in <paramref name="directory"/> and its <c>build</c> subfolder.
    /// </summary>
    /// <remarks>
    /// A folder contains a build if it has a <c>Build.cs</c> file. A <c>Build.cs</c> with <c>#:</c> directives, such as
    /// <c>#:package Baun@0.1.0</c>, is a single-file build. Otherwise the build is the single project file next to it.
    /// </remarks>
    public static BuildLocation? Find(AbsolutePath directory)
    {
        foreach (string folder in _searchFolders)
        {
            AbsolutePath path = directory / folder;
            AbsolutePath buildFile = path / BuildFileName;

            if (!File.Exists(buildFile))
            {
                continue;
            }

            if (IsSingleFileBuild(buildFile))
            {
                return new BuildLocation(buildFile, ProjectFile: null);
            }

            var projectFiles = Directory.GetFiles(path, "*.csproj");

            if (projectFiles.Length == 1)
            {
                return new BuildLocation(buildFile, (AbsolutePath)projectFiles[0]);
            }
        }

        return null;
    }

    /// <summary>
    /// Returns whether the file starts with <c>#:</c> directives (after an optional shebang, comments and blank lines).
    /// </summary>
    internal static bool IsSingleFileBuild(AbsolutePath buildFile)
    {
        foreach (var rawLine in File.ReadLines(buildFile))
        {
            var line = rawLine.Trim();

            if (line.Length == 0 || line.StartsWith("#!", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            return line.StartsWith("#:", StringComparison.Ordinal);
        }

        return false;
    }
}
