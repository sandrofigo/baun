namespace Baun;

/// <summary>
/// The exception that <see cref="Tool"/> throws when a tool exits with a code other than 0.
/// </summary>
public sealed class ToolException : Exception
{
    internal ToolException(string fileName, ToolResult result)
        : base($"{fileName} failed with exit code {result.ExitCode}.")
    {
        FileName = fileName;
        Result = result;
    }

    /// <summary>
    /// Gets the tool that failed.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Gets what the tool wrote, with its exit code.
    /// </summary>
    public ToolResult Result { get; }
}
