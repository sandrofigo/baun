namespace Baun.Git;

/// <summary>
/// Runs <c>git</c> and reads information about the repository, such as <see cref="GetCurrentTagAsync"/> and
/// <see cref="GetCurrentBranchAsync"/>. It writes the output of git with <see cref="Logger"/>, which logs its errors and
/// warnings as such.
/// </summary>
public static class Git
{
    /// <summary>
    /// The file name of the tool, <c>git</c>.
    /// </summary>
    public const string FileName = "git";

    private static ToolLogger logger = new GitLogger();

    /// <summary>
    /// Gets or sets the logger of the commands created from now on. Defaults to a <see cref="GitLogger"/>. Set it to a
    /// class derived from <see cref="GitLogger"/> to change how the output of all <c>git</c> commands is logged, or use
    /// <see cref="ToolCommand.Logger"/> to change it for one command only.
    /// </summary>
    public static ToolLogger Logger
    {
        get => logger;
        set => logger = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Returns a command that runs <c>git</c> with <paramref name="arguments"/>, to add more arguments to and run with
    /// <see cref="ToolCommand.RunAsync"/>.
    /// </summary>
    /// <param name="arguments">The arguments, each passed as it is, so an argument with spaces doesn't need quotes.</param>
    public static ToolCommand Command(params string[] arguments) =>
        Tool.Command(FileName).Logger(Logger).Arguments(arguments);

    /// <summary>
    /// Runs <c>git</c> with <paramref name="arguments"/> in the current directory, and throws if it fails.
    /// </summary>
    /// <param name="arguments">The arguments, each passed as it is, so an argument with spaces doesn't need quotes.</param>
    /// <returns>What git wrote.</returns>
    /// <exception cref="ToolException">git exited with a code other than 0.</exception>
    public static Task<ToolResult> RunAsync(params string[] arguments) => Command(arguments).RunAsync();

    /// <summary>
    /// Runs <c>git</c> with <paramref name="arguments"/>, and throws if it fails.
    /// </summary>
    /// <param name="arguments">The arguments, each passed as it is, so an argument with spaces doesn't need quotes.</param>
    /// <param name="workingDirectory">The directory to run in, or <see langword="null"/> for the current one.</param>
    /// <param name="cancellationToken">Stops git when it's canceled.</param>
    /// <returns>What git wrote.</returns>
    /// <exception cref="ToolException">git exited with a code other than 0.</exception>
    public static Task<ToolResult> RunAsync(
        IEnumerable<string> arguments,
        AbsolutePath? workingDirectory = null,
        CancellationToken cancellationToken = default) =>
        Command().Arguments(arguments).WorkingDirectory(workingDirectory).RunAsync(cancellationToken);

    /// <summary>
    /// Returns the tag of the commit that is checked out, such as <c>v1.2.3</c>, or <see langword="null"/> if it has no
    /// tag. If it has several, it returns the one with the highest version.
    /// </summary>
    /// <param name="workingDirectory">A directory in the repository, or <see langword="null"/> for the current one.</param>
    /// <param name="cancellationToken">Stops git when it's canceled.</param>
    /// <exception cref="ToolException">git failed, for example outside a repository.</exception>
    public static async Task<string?> GetCurrentTagAsync(AbsolutePath? workingDirectory = null, CancellationToken cancellationToken = default)
    {
        ToolResult result = await RunAsync(["tag", "--points-at", "HEAD", "--sort=-version:refname"], workingDirectory, cancellationToken);

        return FirstLine(result);
    }

    /// <summary>
    /// Returns the branch that is checked out, such as <c>main</c>, or <see langword="null"/> if no branch is checked out,
    /// such as when a CI server checks out a tag or a pull request.
    /// </summary>
    /// <param name="workingDirectory">A directory in the repository, or <see langword="null"/> for the current one.</param>
    /// <param name="cancellationToken">Stops git when it's canceled.</param>
    /// <exception cref="ToolException">git failed, for example outside a repository.</exception>
    public static async Task<string?> GetCurrentBranchAsync(AbsolutePath? workingDirectory = null, CancellationToken cancellationToken = default)
    {
        ToolResult result = await RunAsync(["branch", "--show-current"], workingDirectory, cancellationToken);

        return FirstLine(result);
    }

    private static string? FirstLine(ToolResult result) =>
        result.StandardOutput.Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);
}
