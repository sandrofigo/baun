namespace Baun.DotNet;

/// <summary>
/// The exception that is thrown when a solution file isn't a valid <c>.sln</c> or <c>.slnx</c> file.
/// </summary>
public sealed class SolutionFormatException : FormatException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SolutionFormatException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the problem.</param>
    /// <param name="innerException">The exception that caused this one, if any.</param>
    public SolutionFormatException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
