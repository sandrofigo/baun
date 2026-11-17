using Baun.Logging;

namespace Baun;

/// <summary>
/// Runs command-line tools, such as <c>dotnet</c> or <c>git</c>, and writes their output to the log.
/// </summary>
/// <remarks>
/// <para>
/// Each run first logs the command as <see cref="LogLevel.Information"/>, such as <c>› dotnet test</c>. Arguments
/// added with <see cref="ToolCommand.Secret"/> are logged as <c>***</c>, also where the tool writes them.
/// </para>
/// <para>
/// Each line the tool writes goes to a <see cref="ToolLogger"/>, which picks its level. The default one, <see cref="Logger"/>,
/// logs each line of the standard output as <see cref="LogLevel.Debug"/>, and each line of the standard error as
/// <see cref="LogLevel.Warning"/>, since many tools write progress there. The lines are logged as they are, so braces
/// in them don't count as message template properties.
/// </para>
/// </remarks>
public static class Tool
{
    private static ToolLogger logger = ToolLogger.Default;

    /// <summary>
    /// Gets or sets the logger of the commands <see cref="Command"/> creates from now on. Defaults to
    /// <see cref="ToolLogger.Default"/>.
    /// </summary>
    public static ToolLogger Logger
    {
        get => logger;
        set => logger = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Returns a command that runs <paramref name="fileName"/>, to add arguments to and run with
    /// <see cref="ToolCommand.RunAsync"/>.
    /// </summary>
    /// <param name="fileName">The tool to run, such as <c>dotnet</c>. It's searched in the <c>PATH</c>.</param>
    public static ToolCommand Command(string fileName) => new ToolCommand(fileName).Logger(Logger);

    /// <summary>
    /// Runs <paramref name="fileName"/> with <paramref name="arguments"/> in the current directory, and throws if it fails.
    /// </summary>
    /// <param name="fileName">The tool to run, such as <c>dotnet</c>. It's searched in the <c>PATH</c>.</param>
    /// <param name="arguments">
    /// The arguments, each passed as it is, so an argument with spaces doesn't need quotes.
    /// </param>
    /// <returns>What the tool wrote.</returns>
    /// <exception cref="ToolException">The tool exited with a code other than 0.</exception>
    public static Task<ToolResult> RunAsync(string fileName, params string[] arguments) =>
        RunAsync(fileName, arguments, workingDirectory: null);

    /// <summary>
    /// Runs <paramref name="fileName"/> with <paramref name="arguments"/>, and throws if it fails.
    /// </summary>
    /// <param name="fileName">The tool to run, such as <c>dotnet</c>. It's searched in the <c>PATH</c>.</param>
    /// <param name="arguments">
    /// The arguments, each passed as it is, so an argument with spaces doesn't need quotes.
    /// </param>
    /// <param name="workingDirectory">The directory to run the tool in, or <see langword="null"/> for the current one.</param>
    /// <param name="cancellationToken">Stops the tool, with the processes it started, when it's canceled.</param>
    /// <returns>What the tool wrote.</returns>
    /// <exception cref="ToolException">The tool exited with a code other than 0.</exception>
    public static Task<ToolResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        AbsolutePath? workingDirectory = null,
        CancellationToken cancellationToken = default) =>
        Command(fileName).Arguments(arguments).WorkingDirectory(workingDirectory).RunAsync(cancellationToken);
}
