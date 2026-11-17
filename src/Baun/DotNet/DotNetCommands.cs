namespace Baun.DotNet;

/// <summary>
/// A <c>dotnet build</c> command, which <see cref="DotNet.Build"/> creates.
/// </summary>
public sealed class DotNetBuildCommand : DotNetMSBuildCommand<DotNetBuildCommand>
{
    internal DotNetBuildCommand(ToolCommand command)
        : base(command)
    {
    }

    /// <summary>
    /// Adds <c>--output</c>, the directory to put the built files in.
    /// </summary>
    /// <param name="directory">The output directory.</param>
    public DotNetBuildCommand Output(string directory) => Arguments("--output", directory);

    /// <inheritdoc/>
    protected override DotNetBuildCommand With(ToolCommand command) => new(command);
}

/// <summary>
/// A <c>dotnet test</c> command, which <see cref="DotNet.Test"/> creates.
/// </summary>
public sealed class DotNetTestCommand : DotNetMSBuildCommand<DotNetTestCommand>
{
    internal DotNetTestCommand(ToolCommand command)
        : base(command)
    {
    }

    /// <summary>
    /// Adds <c>--solution</c>, the solution to test with Microsoft.Testing.Platform.
    /// </summary>
    /// <param name="solution">The path of the solution.</param>
    public DotNetTestCommand Solution(string solution) => Arguments("--solution", solution);

    /// <summary>
    /// Adds <c>--project</c>, the project to test with Microsoft.Testing.Platform.
    /// </summary>
    /// <param name="project">The path of the project.</param>
    public DotNetTestCommand Project(string project) => Arguments("--project", project);

    /// <summary>
    /// Adds <c>--no-build</c>, so the tests run without building first.
    /// </summary>
    public DotNetTestCommand NoBuild() => Arguments("--no-build");

    /// <inheritdoc/>
    protected override DotNetTestCommand With(ToolCommand command) => new(command);
}

/// <summary>
/// A <c>dotnet pack</c> command, which <see cref="DotNet.Pack"/> creates.
/// </summary>
public sealed class DotNetPackCommand : DotNetMSBuildCommand<DotNetPackCommand>
{
    internal DotNetPackCommand(ToolCommand command)
        : base(command)
    {
    }

    /// <summary>
    /// Adds <c>--output</c>, the directory to put the packages in.
    /// </summary>
    /// <param name="directory">The output directory.</param>
    public DotNetPackCommand Output(string directory) => Arguments("--output", directory);

    /// <summary>
    /// Adds <c>--no-build</c>, so the packages are created without building first.
    /// </summary>
    public DotNetPackCommand NoBuild() => Arguments("--no-build");

    /// <summary>
    /// Sets the version of the packages with <c>-p:Version=...</c>, such as <c>1.2.3</c>.
    /// </summary>
    /// <param name="version">The version.</param>
    public DotNetPackCommand Version(string version) => Property("Version", version);

    /// <inheritdoc/>
    protected override DotNetPackCommand With(ToolCommand command) => new(command);
}

/// <summary>
/// A <c>dotnet publish</c> command, which <see cref="DotNet.Publish"/> creates.
/// </summary>
public sealed class DotNetPublishCommand : DotNetMSBuildCommand<DotNetPublishCommand>
{
    internal DotNetPublishCommand(ToolCommand command)
        : base(command)
    {
    }

    /// <summary>
    /// Adds <c>--output</c>, the directory to publish to.
    /// </summary>
    /// <param name="directory">The output directory.</param>
    public DotNetPublishCommand Output(string directory) => Arguments("--output", directory);

    /// <summary>
    /// Adds <c>--no-build</c>, so the files are published without building first.
    /// </summary>
    public DotNetPublishCommand NoBuild() => Arguments("--no-build");

    /// <summary>
    /// Adds <c>--self-contained</c> or <c>--no-self-contained</c>, whether to publish the .NET runtime with the app.
    /// </summary>
    /// <param name="selfContained">Whether the app is self-contained.</param>
    public DotNetPublishCommand SelfContained(bool selfContained = true) =>
        Arguments(selfContained ? "--self-contained" : "--no-self-contained");

    /// <inheritdoc/>
    protected override DotNetPublishCommand With(ToolCommand command) => new(command);
}

/// <summary>
/// A <c>dotnet restore</c> command, which <see cref="DotNet.Restore"/> creates.
/// </summary>
public sealed class DotNetRestoreCommand : DotNetMSBuildCommand<DotNetRestoreCommand>
{
    internal DotNetRestoreCommand(ToolCommand command)
        : base(command)
    {
    }

    /// <inheritdoc/>
    protected override DotNetRestoreCommand With(ToolCommand command) => new(command);
}

/// <summary>
/// A <c>dotnet clean</c> command, which <see cref="DotNet.Clean"/> creates.
/// </summary>
public sealed class DotNetCleanCommand : DotNetMSBuildCommand<DotNetCleanCommand>
{
    internal DotNetCleanCommand(ToolCommand command)
        : base(command)
    {
    }

    /// <inheritdoc/>
    protected override DotNetCleanCommand With(ToolCommand command) => new(command);
}

/// <summary>
/// A <c>dotnet nuget push</c> command, which <see cref="DotNet.NuGetPush"/> creates.
/// </summary>
public sealed class DotNetNuGetPushCommand : ToolCommand<DotNetNuGetPushCommand>
{
    internal DotNetNuGetPushCommand(ToolCommand command)
        : base(command)
    {
    }

    /// <summary>
    /// Adds <c>--api-key</c> with <paramref name="apiKey"/> as a secret, so it's logged as <c>***</c>.
    /// </summary>
    /// <param name="apiKey">The API key of the package source.</param>
    public DotNetNuGetPushCommand ApiKey(string apiKey) => Arguments("--api-key").Secret(apiKey);

    /// <summary>
    /// Adds <c>--source</c>, the package source to push to, such as <c>https://api.nuget.org/v3/index.json</c>.
    /// </summary>
    /// <param name="source">The URL or name of the package source.</param>
    public DotNetNuGetPushCommand Source(string source) => Arguments("--source", source);

    /// <summary>
    /// Adds <c>--skip-duplicate</c>, so a package whose version already exists is skipped instead of failing the push.
    /// </summary>
    public DotNetNuGetPushCommand SkipDuplicate() => Arguments("--skip-duplicate");

    /// <inheritdoc/>
    protected override DotNetNuGetPushCommand With(ToolCommand command) => new(command);
}
