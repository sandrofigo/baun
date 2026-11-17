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
