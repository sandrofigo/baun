namespace Baun.Tools.DotNet;

/// <summary>
/// The base of <c>dotnet</c> commands that run MSBuild, such as <c>dotnet build</c>, with the options they share.
/// </summary>
/// <remarks>
/// It only has options that every <c>dotnet</c> command running MSBuild accepts, including <c>dotnet restore</c> and <c>dotnet clean</c>.
/// Options that only some commands have, such as <c>--configuration</c>, belong on those commands.
/// </remarks>
/// <typeparam name="TSelf">The derived command, which the methods return.</typeparam>
public abstract class DotNetMSBuildCommand<TSelf> : ToolCommand<TSelf>
    where TSelf : DotNetMSBuildCommand<TSelf>
{
    /// <summary>
    /// Creates a command that runs <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command with the arguments so far.</param>
    protected DotNetMSBuildCommand(ToolCommand command)
        : base(command)
    {
    }

    /// <summary>
    /// Adds <c>--runtime</c>, such as <c>linux-x64</c>.
    /// </summary>
    /// <param name="runtime">The runtime identifier.</param>
    public TSelf Runtime(string runtime) => Arguments("--runtime", runtime);

    /// <summary>
    /// Sets the MSBuild property <paramref name="name"/> with <c>-p:name=value</c>.
    /// </summary>
    /// <param name="name">The name of the property, such as <c>DebugType</c>.</param>
    /// <param name="value">The value of the property.</param>
    public TSelf Property(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);

        return Arguments($"-p:{name}={value}");
    }

    /// <summary>
    /// Sets the version of the assemblies and packages with <c>-p:Version=...</c>, such as <c>1.2.3</c>.
    /// </summary>
    /// <param name="version">The version.</param>
    public TSelf Version(string version) => Property("Version", version);

    /// <summary>
    /// Sets the MSBuild property <c>ContinuousIntegrationBuild</c>, which makes the build deterministic, such as for a release.
    /// </summary>
    /// <param name="continuousIntegrationBuild">Whether the build runs in continuous integration.</param>
    public TSelf ContinuousIntegrationBuild(bool continuousIntegrationBuild = true) =>
        Property("ContinuousIntegrationBuild", continuousIntegrationBuild ? "true" : "false");

    /// <summary>
    /// Adds <c>--verbosity</c>, which sets how much MSBuild writes.
    /// </summary>
    /// <param name="verbosity">The verbosity.</param>
    public TSelf Verbosity(DotNetVerbosity verbosity) => Arguments("--verbosity", verbosity switch
    {
        DotNetVerbosity.Quiet => "quiet",
        DotNetVerbosity.Minimal => "minimal",
        DotNetVerbosity.Normal => "normal",
        DotNetVerbosity.Detailed => "detailed",
        DotNetVerbosity.Diagnostic => "diagnostic",
        _ => throw new ArgumentOutOfRangeException(nameof(verbosity), verbosity, null),
    });
}
