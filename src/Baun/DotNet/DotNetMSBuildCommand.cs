namespace Baun.DotNet;

/// <summary>
/// The base of <c>dotnet</c> commands that run MSBuild, such as <c>dotnet build</c>, with the options they share.
/// </summary>
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
    /// Adds <c>--configuration</c>, such as <c>Release</c>.
    /// </summary>
    /// <param name="configuration">The build configuration.</param>
    public TSelf Configuration(string configuration) => Arguments("--configuration", configuration);

    /// <summary>
    /// Adds <c>--framework</c>, such as <c>net10.0</c>.
    /// </summary>
    /// <param name="framework">The target framework.</param>
    public TSelf Framework(string framework) => Arguments("--framework", framework);

    /// <summary>
    /// Adds <c>--runtime</c>, such as <c>linux-x64</c>.
    /// </summary>
    /// <param name="runtime">The runtime identifier.</param>
    public TSelf Runtime(string runtime) => Arguments("--runtime", runtime);

    /// <summary>
    /// Sets the MSBuild property <paramref name="name"/> with <c>-p:name=value</c>.
    /// </summary>
    /// <param name="name">The name of the property, such as <c>ContinuousIntegrationBuild</c>.</param>
    /// <param name="value">The value of the property.</param>
    public TSelf Property(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);

        return Arguments($"-p:{name}={value}");
    }

    /// <summary>
    /// Adds <c>--no-restore</c>, so the command doesn't restore packages first.
    /// </summary>
    public TSelf NoRestore() => Arguments("--no-restore");

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
