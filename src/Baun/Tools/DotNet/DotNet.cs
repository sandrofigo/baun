namespace Baun.Tools.DotNet;

/// <summary>
/// Runs <c>dotnet</c>, with typed commands such as <see cref="Build"/>, <see cref="Test"/> and <see cref="Pack"/>, and
/// writes its output with <see cref="Logger"/>, which logs errors and warnings of MSBuild as such.
/// </summary>
/// <remarks>
/// Each typed command adds options with methods of its own, such as <c>Configuration("Release")</c>, and still takes
/// any argument with <c>Arguments</c> and <c>Secret</c>. Run it with <c>RunAsync</c>:
/// <code>
/// await DotNet.Pack(Solution).Configuration("Release").Output(ArtifactsDirectory).Version("1.2.3").RunAsync();
/// </code>
/// For other commands, use <see cref="Command"/> or <see cref="RunAsync(string[])"/>.
/// </remarks>
public static class DotNet
{
    /// <summary>
    /// The file name of the tool, <c>dotnet</c>.
    /// </summary>
    public const string FileName = "dotnet";

    private static ToolLogger logger = new DotNetLogger();

    /// <summary>
    /// Gets or sets the logger of the commands created from now on. Defaults to a <see cref="DotNetLogger"/>. Set it to
    /// a class derived from <see cref="DotNetLogger"/> to change how the output of all <c>dotnet</c> commands is
    /// logged, or use <c>Logger(...)</c> of a command to change it for that command only.
    /// </summary>
    public static ToolLogger Logger
    {
        get => logger;
        set => logger = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Returns a command that runs <c>dotnet</c> with <paramref name="arguments"/>, to add more arguments to and run with
    /// <see cref="ToolCommand.RunAsync"/>.
    /// </summary>
    /// <param name="arguments">The arguments, each passed as it is, so an argument with spaces doesn't need quotes.</param>
    public static ToolCommand Command(params string[] arguments) =>
        Tool.Command(FileName).Logger(Logger).Arguments(arguments);

    /// <summary>
    /// Runs <c>dotnet</c> with <paramref name="arguments"/> in the current directory, and throws if it fails.
    /// </summary>
    /// <param name="arguments">The arguments, each passed as it is, so an argument with spaces doesn't need quotes.</param>
    /// <returns>What <c>dotnet</c> wrote.</returns>
    /// <exception cref="ToolException"><c>dotnet</c> exited with a code other than 0.</exception>
    public static Task<ToolResult> RunAsync(params string[] arguments) => Command(arguments).RunAsync();

    /// <summary>
    /// Runs <c>dotnet</c> with <paramref name="arguments"/>, and throws if it fails.
    /// </summary>
    /// <param name="arguments">The arguments, each passed as it is, so an argument with spaces doesn't need quotes.</param>
    /// <param name="workingDirectory">The directory to run in, or <see langword="null"/> for the current one.</param>
    /// <param name="cancellationToken">Stops <c>dotnet</c>, with the processes it started, when it's canceled.</param>
    /// <returns>What <c>dotnet</c> wrote.</returns>
    /// <exception cref="ToolException"><c>dotnet</c> exited with a code other than 0.</exception>
    public static Task<ToolResult> RunAsync(
        IEnumerable<string> arguments,
        AbsolutePath? workingDirectory = null,
        CancellationToken cancellationToken = default) =>
        Command().Arguments(arguments).WorkingDirectory(workingDirectory).RunAsync(cancellationToken);

    /// <summary>
    /// Returns a <c>dotnet build</c> command.
    /// </summary>
    /// <param name="target">The project or solution to build, or <see langword="null"/> for the one in the working directory.</param>
    public static DotNetBuildCommand Build(string? target = null) => new(Verb("build", target));

    /// <summary>
    /// Returns a <c>dotnet test</c> command.
    /// </summary>
    /// <param name="target">
    /// The project or solution to test, or <see langword="null"/> for the one in the working directory. With
    /// Microsoft.Testing.Platform, leave it out and use <see cref="DotNetTestCommand.Solution"/> or
    /// <see cref="DotNetTestCommand.Project"/> instead.
    /// </param>
    public static DotNetTestCommand Test(string? target = null) => new(Verb("test", target));

    /// <summary>
    /// Returns a <c>dotnet pack</c> command.
    /// </summary>
    /// <param name="target">The project or solution to pack, or <see langword="null"/> for the one in the working directory.</param>
    public static DotNetPackCommand Pack(string? target = null) => new(Verb("pack", target));

    /// <summary>
    /// Returns a <c>dotnet publish</c> command.
    /// </summary>
    /// <param name="target">The project or solution to publish, or <see langword="null"/> for the one in the working directory.</param>
    public static DotNetPublishCommand Publish(string? target = null) => new(Verb("publish", target));

    /// <summary>
    /// Returns a <c>dotnet restore</c> command.
    /// </summary>
    /// <param name="target">The project or solution to restore, or <see langword="null"/> for the one in the working directory.</param>
    public static DotNetRestoreCommand Restore(string? target = null) => new(Verb("restore", target));

    /// <summary>
    /// Returns a <c>dotnet clean</c> command.
    /// </summary>
    /// <param name="target">The project or solution to clean, or <see langword="null"/> for the one in the working directory.</param>
    public static DotNetCleanCommand Clean(string? target = null) => new(Verb("clean", target));

    /// <summary>
    /// Returns a <c>dotnet nuget push</c> command.
    /// </summary>
    /// <param name="package">The package to push, or a pattern such as <c>artifacts/*.nupkg</c>.</param>
    public static DotNetNuGetPushCommand NuGetPush(string package)
    {
        ArgumentException.ThrowIfNullOrEmpty(package);

        return new(Command("nuget", "push", package));
    }

    private static ToolCommand Verb(string verb, string? target) =>
        target is null ? Command(verb) : Command(verb, target);
}
