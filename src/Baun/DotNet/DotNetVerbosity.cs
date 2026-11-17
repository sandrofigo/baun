namespace Baun.DotNet;

/// <summary>
/// The verbosity of MSBuild, which <c>--verbosity</c> of commands such as <c>dotnet build</c> sets.
/// </summary>
public enum DotNetVerbosity
{
    /// <summary>
    /// <c>quiet</c>: only errors and warnings.
    /// </summary>
    Quiet,

    /// <summary>
    /// <c>minimal</c>: errors, warnings and the most important messages.
    /// </summary>
    Minimal,

    /// <summary>
    /// <c>normal</c>.
    /// </summary>
    Normal,

    /// <summary>
    /// <c>detailed</c>.
    /// </summary>
    Detailed,

    /// <summary>
    /// <c>diagnostic</c>: everything.
    /// </summary>
    Diagnostic,
}
