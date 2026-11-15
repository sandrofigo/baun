namespace Baun.CI;

/// <summary>
/// A category of changes in a release of a changelog, such as <c>Added</c> or <c>Fixed</c>.
/// </summary>
/// <param name="Name">
/// The name of the category: <c>Added</c>, <c>Changed</c>, <c>Deprecated</c>, <c>Removed</c>, <c>Fixed</c> or <c>Security</c>.
/// </param>
/// <param name="Entries">The entries of the category, without their leading <c>- </c>. An entry can span several lines, which end with <c>\n</c>.</param>
public sealed record ChangelogCategory(string Name, IReadOnlyList<string> Entries)
{
    /// <summary>
    /// Returns the category as Markdown: its <c>### Name</c> heading followed by its entries as a list.
    /// </summary>
    public override string ToString() =>
        $"### {Name}\n\n{string.Join("\n", Entries.Select(entry => "- " + entry.ReplaceLineEndings("\n  ")))}";
}
