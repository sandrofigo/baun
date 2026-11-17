using System.Collections.Immutable;
using System.Diagnostics;
using Baun.Logging;

namespace Baun;

/// <summary>
/// Runs command-line tools, such as <c>dotnet</c> or <c>git</c>, and writes their output to the log.
/// </summary>
/// <remarks>
/// <para>
/// Each run first logs the command as <see cref="LogLevel.Information"/>, such as <c>&gt; dotnet test</c>. Arguments
/// added with <see cref="ToolCommand.Secret"/> are logged as <c>***</c>, also where the tool writes them.
/// </para>
/// <para>
/// Each line a tool writes to its standard output is logged as <see cref="LogLevel.Debug"/>, and each line it writes to
/// its standard error as <see cref="LogLevel.Warning"/>, since many tools write progress there. The lines are logged as
/// they are, so braces in them don't count as message template properties.
/// </para>
/// </remarks>
public static class Tool
{
    /// <summary>
    /// Returns a command that runs <paramref name="fileName"/>, to add arguments to and run with
    /// <see cref="ToolCommand.RunAsync"/>.
    /// </summary>
    /// <param name="fileName">The tool to run, such as <c>dotnet</c>. It's searched in the <c>PATH</c>.</param>
    public static ToolCommand Command(string fileName) => new(fileName);

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

/// <summary>
/// A command-line tool with its arguments, which <see cref="Tool.Command"/> creates.
/// </summary>
/// <remarks>
/// A command doesn't change: each method returns a new command, so one can be the base of others.
/// <see cref="ToString"/> returns the command as it's logged, with secrets as <c>***</c>.
/// </remarks>
public sealed class ToolCommand
{
    private const string Mask = "***";

    private readonly ImmutableArray<ToolArgument> arguments;

    private readonly AbsolutePath? workingDirectory;

    internal ToolCommand(string fileName)
        : this(fileName, [], null)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
    }

    private ToolCommand(string fileName, ImmutableArray<ToolArgument> arguments, AbsolutePath? workingDirectory)
    {
        FileName = fileName;
        this.arguments = arguments;
        this.workingDirectory = workingDirectory;
    }

    /// <summary>
    /// Gets the tool to run, such as <c>dotnet</c>.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Returns a command with <paramref name="arguments"/> added.
    /// </summary>
    /// <param name="arguments">The arguments, each passed as it is, so an argument with spaces doesn't need quotes.</param>
    public ToolCommand Arguments(params string[] arguments) => Arguments((IEnumerable<string>)arguments);

    /// <summary>
    /// Returns a command with <paramref name="arguments"/> added.
    /// </summary>
    /// <param name="arguments">The arguments, each passed as it is, so an argument with spaces doesn't need quotes.</param>
    public ToolCommand Arguments(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var added = new List<ToolArgument>();

        foreach (string argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument, nameof(arguments));
            added.Add(new ToolArgument(argument, IsSecret: false));
        }

        return new(FileName, this.arguments.AddRange(added), workingDirectory);
    }

    /// <summary>
    /// Returns a command with the secret <paramref name="value"/>, such as an API key, added as an argument. It's
    /// passed as it is, but logged as <c>***</c>, also in the output of the tool.
    /// </summary>
    /// <param name="value">The secret.</param>
    public ToolCommand Secret(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        return new(FileName, arguments.Add(new ToolArgument(value, IsSecret: true)), workingDirectory);
    }

    /// <summary>
    /// Returns a command that runs in <paramref name="directory"/>.
    /// </summary>
    /// <param name="directory">The directory to run the tool in, or <see langword="null"/> for the current one.</param>
    public ToolCommand WorkingDirectory(AbsolutePath? directory) => new(FileName, arguments, directory);

    /// <summary>
    /// Runs the tool, and throws if it fails.
    /// </summary>
    /// <param name="cancellationToken">Stops the tool, with the processes it started, when it's canceled.</param>
    /// <returns>What the tool wrote, as it wrote it, so including secrets.</returns>
    /// <exception cref="ToolException">The tool exited with a code other than 0.</exception>
    public async Task<ToolResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(FileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        if (workingDirectory is not null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (ToolArgument argument in arguments)
        {
            startInfo.ArgumentList.Add(argument.Value);
        }

        Log.Information("> {Command}", ToString());

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Couldn't start {FileName}.");

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
            throw new ToolException(FileName, result);
        }

        return result;
    }

    /// <summary>
    /// Returns the command as it's logged, with secrets as <c>***</c> and arguments with spaces in quotes.
    /// </summary>
    public override string ToString() =>
        string.Join(' ', arguments.Select(argument => argument.IsSecret ? Mask : Quote(argument.Value)).Prepend(Quote(FileName)));

    private static string Quote(string argument) =>
        argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"')
            ? argument
            : $"\"{argument.Replace("\"", "\\\"")}\"";

    private string MaskSecrets(string line)
    {
        foreach (ToolArgument argument in arguments)
        {
            if (argument.IsSecret)
            {
                line = line.Replace(argument.Value, Mask, StringComparison.Ordinal);
            }
        }

        return line;
    }

    private async Task ReadLinesAsync(StreamReader reader, List<string> lines, LogLevel level, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lines.Add(line);
            Log.WriteText(level, MaskSecrets(line));
        }
    }

    private readonly record struct ToolArgument(string Value, bool IsSecret);
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
