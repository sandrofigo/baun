namespace Baun.CI;

/// <summary>
/// The exception that is thrown when a changelog isn't in the <see href="https://keepachangelog.com/">Keep a Changelog</see> format.
/// </summary>
public sealed class ChangelogFormatException : FormatException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChangelogFormatException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the problem, including the line.</param>
    /// <param name="lineNumber">The 1-based number of the line with the problem.</param>
    /// <param name="innerException">The exception that caused this one, if any.</param>
    public ChangelogFormatException(string message, int lineNumber, Exception? innerException = null)
        : base(message, innerException)
    {
        LineNumber = lineNumber;
    }

    /// <summary>
    /// Gets the 1-based number of the line with the problem.
    /// </summary>
    public int LineNumber { get; }
}
