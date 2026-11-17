using System.Text.RegularExpressions;

partial class Build
{
    /// <summary>
    /// Gets the version from the tag of the current commit, for example 1.2.3 from v1.2.3.
    /// </summary>
    async Task<string> GetVersionAsync()
    {
        string tag = await Git.GetCurrentTagAsync()
            ?? throw new InvalidOperationException("This step only runs on a commit with a release tag, such as v1.2.3.");

        if (!Regex.IsMatch(tag, @"^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$"))
        {
            throw new InvalidOperationException($"The tag '{tag}' is not a version such as v1.2.3 or v1.2.3-preview.1.");
        }

        return tag[1..];
    }
}
