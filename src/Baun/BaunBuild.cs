namespace Baun;

/// <summary>
/// Runs baun builds.
/// </summary>
/// <remarks>
/// A build usually doesn't call this itself. When a build has no top-level statements and no <c>Main</c> method,
/// the baun source generator adds an entry point that calls <see cref="RunAsync{TBuild}"/> for its build class.
/// Call it yourself to run code before or after the build:
/// <code>
/// Log.Information("Starting");
/// await BaunBuild.RunAsync&lt;Build&gt;();
/// </code>
/// </remarks>
public static class BaunBuild
{
    private static readonly Lazy<AbsolutePath> rootDirectory = new(() => FindRootDirectory((AbsolutePath)Environment.CurrentDirectory));

    private static readonly Lazy<AbsolutePath> tempDirectory = new(() => FindTempDirectory((AbsolutePath)Environment.CurrentDirectory));

    /// <summary>
    /// Gets the root directory of the repository: the nearest folder, from the current directory up through its parents,
    /// that contains a <c>.baun</c> folder. Without one, it's the current directory.
    /// </summary>
    /// <remarks>
    /// It's found once, the first time it's used, so changing the current directory later doesn't change it. Build
    /// classes also get a <c>RootDirectory</c> property from the baun source generator that returns this value.
    /// </remarks>
    public static AbsolutePath RootDirectory => rootDirectory.Value;

    /// <summary>
    /// Returns the root directory of the repository <paramref name="directory"/> is in, as <see cref="RootDirectory"/> does
    /// for the current directory.
    /// </summary>
    internal static AbsolutePath FindRootDirectory(AbsolutePath directory) =>
        RepositoryRoot.TryFind(directory) ?? directory;

    /// <summary>
    /// Gets the temp directory of baun: <c>.baun/temp</c> in the <see cref="RootDirectory"/>, which <c>baun init</c> adds
    /// to <c>.baun/.gitignore</c>. Without a <c>.baun</c> folder, it's the folder <c>baun</c> in the temp directory of the
    /// system. It's created if it doesn't exist.
    /// </summary>
    /// <remarks>
    /// Use it for files a build only needs while it runs, such as downloads or generated settings. baun doesn't delete
    /// what's in it, so the files stay available to look at after a run. Build classes also get a <c>TempDirectory</c>
    /// property from the baun source generator that returns this value.
    /// </remarks>
    public static AbsolutePath TempDirectory => tempDirectory.Value.CreateDirectory();

    /// <summary>
    /// Returns the temp directory of baun for <paramref name="directory"/>, as <see cref="TempDirectory"/> does for the
    /// current directory, without creating it.
    /// </summary>
    internal static AbsolutePath FindTempDirectory(AbsolutePath directory) =>
        RepositoryRoot.TryFind(directory) is { } root
            ? root / RepositoryRoot.FolderName / RepositoryRoot.TempFolderName
            : (AbsolutePath)Path.GetTempPath() / "baun";

    /// <summary>
    /// Creates an empty file with a unique name in <see cref="TempDirectory"/>, like <see cref="Path.GetTempFileName"/>.
    /// </summary>
    /// <remarks>baun doesn't delete the file. Delete it yourself when you no longer need it.</remarks>
    /// <param name="extension">The extension of the file, such as <c>.json</c> or <c>json</c>. Defaults to <c>.tmp</c>.</param>
    /// <returns>The path of the file.</returns>
    /// <exception cref="ArgumentException"><paramref name="extension"/> contains a directory separator or an invalid character.</exception>
    public static AbsolutePath CreateTempFile(string? extension = null) => CreateTempFile(TempDirectory, extension);

    /// <summary>
    /// Creates an empty file with a unique name in <paramref name="directory"/>, creating the directory if needed.
    /// </summary>
    internal static AbsolutePath CreateTempFile(AbsolutePath directory, string? extension)
    {
        extension = string.IsNullOrEmpty(extension) ? ".tmp" : extension.StartsWith('.') ? extension : "." + extension;
        ThrowIfInvalidName(extension, nameof(extension));
        directory.CreateDirectory();

        while (true)
        {
            AbsolutePath path = directory / (Path.GetFileNameWithoutExtension(Path.GetRandomFileName()) + extension);

            try
            {
                new FileStream(path, FileMode.CreateNew, FileAccess.Write).Dispose();
                return path;
            }
            catch (IOException) when (path.Exists)
            {
                // The name is taken: try another one.
            }
        }
    }

    /// <summary>
    /// Creates an empty directory with a unique name in <see cref="TempDirectory"/>, like
    /// <see cref="Directory.CreateTempSubdirectory"/>.
    /// </summary>
    /// <remarks>baun doesn't delete the directory. Delete it yourself when you no longer need it.</remarks>
    /// <param name="prefix">The start of the name of the directory, such as <c>download-</c>.</param>
    /// <returns>The path of the directory.</returns>
    /// <exception cref="ArgumentException"><paramref name="prefix"/> contains a directory separator or an invalid character.</exception>
    public static AbsolutePath CreateTempDirectory(string? prefix = null) => CreateTempDirectory(TempDirectory, prefix);

    /// <summary>
    /// Creates an empty directory with a unique name in <paramref name="directory"/>, creating the directory if needed.
    /// </summary>
    internal static AbsolutePath CreateTempDirectory(AbsolutePath directory, string? prefix)
    {
        prefix ??= string.Empty;
        ThrowIfInvalidName(prefix, nameof(prefix));
        directory.CreateDirectory();

        while (true)
        {
            AbsolutePath path = directory / (prefix + Path.GetFileNameWithoutExtension(Path.GetRandomFileName()));

            if (!path.Exists)
            {
                return path.CreateDirectory();
            }
        }
    }

    /// <summary>
    /// Throws if <paramref name="name"/> can't be part of a file name, such as when it contains a directory separator.
    /// </summary>
    private static void ThrowIfInvalidName(string name, string parameterName)
    {
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
        {
            throw new ArgumentException($"'{name}' can't be part of a file name.", parameterName);
        }
    }

    /// <summary>
    /// Runs the steps named on the command line, or the step marked with <see cref="EntryAttribute"/> if none are named.
    /// </summary>
    /// <remarks>
    /// The command-line arguments are read from <see cref="Environment.GetCommandLineArgs"/>.
    /// If the build fails, <see cref="Environment.ExitCode"/> is set to a non-zero value.
    /// </remarks>
    /// <typeparam name="TBuild">The build class. It must be <c>partial</c> so baun can complete it.</typeparam>
    public static async Task RunAsync<TBuild>() where TBuild : IBaunBuild, new()
    {
        var build = new TBuild();
        var arguments = Environment.GetCommandLineArgs()[1..];

        var exitCode = await new BuildRunner(build, BaunConsole.Create()).RunAsync(arguments);

        if (exitCode != 0)
        {
            Environment.ExitCode = exitCode;
        }
    }
}
