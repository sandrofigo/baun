namespace Baun.Logging;

/// <summary>
/// A command that runs, which the console writes after a chevron, such as <c>› dotnet test</c>.
/// </summary>
/// <remarks>
/// It's a value of a message instead of text, so the console can pick the chevron it supports.
/// </remarks>
/// <param name="Text">The command, as it's written.</param>
internal sealed record CommandLine(string Text)
{
    public override string ToString() => Text;
}
