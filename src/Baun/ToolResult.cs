namespace Baun;

/// <summary>
/// What a tool that <see cref="Tool"/> ran wrote.
/// </summary>
/// <param name="ExitCode">The exit code of the tool.</param>
/// <param name="StandardOutput">The lines the tool wrote to its standard output.</param>
/// <param name="StandardError">The lines the tool wrote to its standard error.</param>
public sealed record ToolResult(int ExitCode, IReadOnlyList<string> StandardOutput, IReadOnlyList<string> StandardError);
