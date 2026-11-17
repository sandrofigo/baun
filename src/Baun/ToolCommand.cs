using System.Collections.Immutable;
using System.Diagnostics;
using Baun.Logging;

namespace Baun;

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

    private readonly ToolLogger logger;

    internal ToolCommand(string fileName)
        : this(fileName, [], null, ToolLogger.Default)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
    }

    private ToolCommand(string fileName, ImmutableArray<ToolArgument> arguments, AbsolutePath? workingDirectory, ToolLogger logger)
    {
        FileName = fileName;
        this.arguments = arguments;
        this.workingDirectory = workingDirectory;
        this.logger = logger;
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

        return new(FileName, this.arguments.AddRange(added), workingDirectory, logger);
    }

    /// <summary>
    /// Returns a command with the secret <paramref name="value"/>, such as an API key, added as an argument. It's
    /// passed as it is, but logged as <c>***</c>, also in the output of the tool.
    /// </summary>
    /// <param name="value">The secret.</param>
    public ToolCommand Secret(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        return new(FileName, arguments.Add(new ToolArgument(value, IsSecret: true)), workingDirectory, logger);
    }

    /// <summary>
    /// Returns a command that runs in <paramref name="directory"/>.
    /// </summary>
    /// <param name="directory">The directory to run the tool in, or <see langword="null"/> for the current one.</param>
    public ToolCommand WorkingDirectory(AbsolutePath? directory) => new(FileName, arguments, directory, logger);

    /// <summary>
    /// Returns a command that writes the output of the tool with <paramref name="logger"/>.
    /// </summary>
    /// <param name="logger">The logger that picks the level of each line the tool writes.</param>
    public ToolCommand Logger(ToolLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        return new(FileName, arguments, workingDirectory, logger);
    }

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

        Log.WriteCommand(ToString());

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Couldn't start {FileName}.");

        var standardOutput = new List<string>();
        var standardError = new List<string>();

        try
        {
            // Both streams are read at the same time, so a tool can't block on a full buffer of the other one.
            await Task.WhenAll(
                ReadLinesAsync(process.StandardOutput, standardOutput, ToolStream.StandardOutput, cancellationToken),
                ReadLinesAsync(process.StandardError, standardError, ToolStream.StandardError, cancellationToken));

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

    private async Task ReadLinesAsync(StreamReader reader, List<string> lines, ToolStream stream, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lines.Add(line);
            logger.Write(new ToolOutputLine(stream, MaskSecrets(line)));
        }
    }

    private readonly record struct ToolArgument(string Value, bool IsSecret);
}
