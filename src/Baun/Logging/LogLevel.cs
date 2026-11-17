namespace Baun;

/// <summary>
/// The level of a log message, from the most detailed to the most severe.
/// </summary>
public enum LogLevel
{
    /// <summary>
    /// Tracing details that are rarely needed.
    /// </summary>
    Verbose,

    /// <summary>
    /// Details that help to find out why something happened.
    /// </summary>
    Debug,

    /// <summary>
    /// What the build is doing. Builds write these by default.
    /// </summary>
    Information,

    /// <summary>
    /// Something unexpected that doesn't stop the build.
    /// </summary>
    Warning,

    /// <summary>
    /// Something that failed.
    /// </summary>
    Error,

    /// <summary>
    /// A failure that the build can't recover from.
    /// </summary>
    Fatal,
}
