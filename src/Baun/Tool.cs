using System.Diagnostics;
using Baun.Logging;

namespace Baun;

/// <summary>
/// Runs command-line tools, such as <c>dotnet</c> or <c>git</c>, and writes their output to the log.
/// </summary>
/// <remarks>
/// Each line a tool writes to its standard output is logged as <see cref="LogLevel.Debug"/>, and each line it writes to
/// its standard error as <see cref="LogLevel.Warning"/>, since many tools write progress there. The lines are logged as
/// they are, so braces in them don't count as message template properties.
/// </remarks>
public static class Tool
{
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
    public static async Task<ToolResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        AbsolutePath? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        if (workingDirectory is not null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Couldn't start {fileName}.");

        var standardOutput = new List<string>();
        var standardError = new List<string>();

        try
        {
            // Both streams are read at the same time, so a tool can't block on a full buffer of the other one.
            await Task.WhenAll(
                ReadLinesAsync(process.StandardOutput, standardOutput, LogLevel.Debug, cancellationToken),
                ReadLinesAsync(process.StandardError, standardError, LogLevel.Warning, cancellationToken));

            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        var result = new ToolResult(process.ExitCode, standardOutput, standardError);

        if (result.ExitCode != 0)
        {
            throw new ToolException(fileName, result);
        }

        return result;
    }

    private static async Task ReadLinesAsync(StreamReader reader, List<string> lines, LogLevel level, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lines.Add(line);
            Log.WriteText(level, line);
        }
    }
}

/// <summary>
/// What a tool that <see cref="Tool"/> ran wrote.
/// </summary>
/// <param name="ExitCode">The exit code of the tool.</param>
/// <param name="StandardOutput">The lines the tool wrote to its standard output.</param>
/// <param name="StandardError">The lines the tool wrote to its standard error.</param>
public sealed record ToolResult(int ExitCode, IReadOnlyList<string> StandardOutput, IReadOnlyList<string> StandardError);

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
