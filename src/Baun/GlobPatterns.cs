namespace Baun;

/// <summary>
/// Reads glob patterns in which a pattern that starts with <c>!</c> excludes what it matches.
/// </summary>
internal static class GlobPatterns
{
    /// <summary>
    /// Splits <paramref name="patterns"/> into the patterns that include and those that exclude, without their
    /// <c>!</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="patterns"/> or one of its patterns is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A pattern is empty.</exception>
    public static (List<string> Includes, List<string> Excludes) Split(string[] patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        var includes = new List<string>();
        var excludes = new List<string>();

        foreach (string pattern in patterns)
        {
            ArgumentException.ThrowIfNullOrEmpty(pattern, nameof(patterns));

            if (pattern.StartsWith('!'))
            {
                excludes.Add(pattern[1..]);
            }
            else
            {
                includes.Add(pattern);
            }
        }

        return (includes, excludes);
    }
}
