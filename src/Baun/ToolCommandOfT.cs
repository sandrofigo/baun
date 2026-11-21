namespace Baun;

/// <summary>
/// The base of typed commands of a tool, such as <c>DotNet.Pack(...)</c>, which add options with methods of their own
/// and still take any argument with <see cref="Arguments(string[])"/> and <see cref="Secret"/>.
/// </summary>
/// <remarks>
/// A command doesn't change: each method returns a new command, so one can be the base of others. To add a typed
/// command for a tool, derive from this class with the derived class as <typeparamref name="TSelf"/>, implement
/// <see cref="With"/>, and add methods that return <c>Arguments(...)</c>.
/// </remarks>
/// <typeparam name="TSelf">The derived command, which the methods return.</typeparam>
public abstract class ToolCommand<TSelf>
    where TSelf : ToolCommand<TSelf>
{
    /// <summary>
    /// Creates a typed command that runs <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command with the arguments so far.</param>
    protected ToolCommand(ToolCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Command = command;
    }

    /// <summary>
    /// Gets the command with the arguments so far.
    /// </summary>
    public ToolCommand Command { get; }

    /// <summary>
    /// Returns a typed command of the same type that runs <paramref name="command"/>.
    /// </summary>
    /// <param name="command">The command with the arguments so far.</param>
    protected abstract TSelf With(ToolCommand command);

    /// <inheritdoc cref="ToolCommand.Arguments(string[])"/>
    public TSelf Arguments(params string[] arguments) => With(Command.Arguments(arguments));

    /// <inheritdoc cref="ToolCommand.Arguments(IEnumerable{string})"/>
    public TSelf Arguments(IEnumerable<string> arguments) => With(Command.Arguments(arguments));

    /// <inheritdoc cref="ToolCommand.Secret"/>
    public TSelf Secret(string value) => With(Command.Secret(value));

    /// <inheritdoc cref="ToolCommand.WorkingDirectory"/>
    public TSelf WorkingDirectory(AbsolutePath? directory) => With(Command.WorkingDirectory(directory));

    /// <inheritdoc cref="ToolCommand.Logger"/>
    public TSelf Logger(ToolLogger logger) => With(Command.Logger(logger));

    /// <inheritdoc cref="ToolCommand.AllowFailure"/>
    public TSelf AllowFailure() => With(Command.AllowFailure());

    /// <inheritdoc cref="ToolCommand.RunAsync"/>
    public Task<ToolResult> RunAsync(CancellationToken cancellationToken = default) => Command.RunAsync(cancellationToken);

    /// <inheritdoc cref="ToolCommand.ToString"/>
    public override string ToString() => Command.ToString();
}
