using System.Text.RegularExpressions;

partial class Build
{
    /// <summary>
    /// Gets the version from the tag, for example 1.2.3 from v1.2.3.
    /// </summary>
    string GetVersion()
    {
        if (Tag is null)
        {
            throw new InvalidOperationException("This step only runs for a release tag (--tag v1.2.3).");
        }

        if (!Regex.IsMatch(Tag, @"^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$"))
        {
            throw new InvalidOperationException($"The tag '{Tag}' is not a version such as v1.2.3 or v1.2.3-preview.1.");
        }

        return Tag[1..];
    }
}
