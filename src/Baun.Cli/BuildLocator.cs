namespace Baun.Cli;

/// <summary>
/// A baun build found on disk.
/// </summary>
/// <param name="BuildFile">The path of the build's <c>Build.cs</c> file.</param>
/// <param name="ProjectFile">The path of the build project, or <see langword="null"/> for a single-file build.</param>
internal sealed record BuildLocation(string BuildFile, string? ProjectFile);

internal static class BuildLocator
{
    private const string BuildFileName = "Build.cs";

    private static readonly string[] SearchFolders = [".", "build"];

    /// <summary>
    /// Looks for a build in <paramref name="directory"/> and its <c>build</c> subfolder.
    /// </summary>
    /// <remarks>
    /// A folder contains a build if it has a <c>Build.cs</c> file. A <c>Build.cs</c> with <c>#:</c> directives, such as
    /// <c>#:package Baun@0.1.0</c>, is a single-file build. Otherwise the build is the single project file next to it.
    /// </remarks>
    public static BuildLocation? Find(string directory)
    {
        foreach (var folder in SearchFolders)
        {
            var path = Path.GetFullPath(Path.Combine(directory, folder));
            var buildFile = Path.Combine(path, BuildFileName);

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
                return new BuildLocation(buildFile, projectFiles[0]);
            }
        }

        return null;
    }

    /// <summary>
    /// Returns whether the file starts with <c>#:</c> directives (after an optional shebang, comments and blank lines).
    /// </summary>
    internal static bool IsSingleFileBuild(string buildFile)
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
