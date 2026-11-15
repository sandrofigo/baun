using System.Diagnostics;
using System.Text.RegularExpressions;

partial class Build
{
    /// <summary>
    /// Runs a program with the console's output, and throws if it fails.
    /// </summary>
    static async Task Run(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName);

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Couldn't start {fileName}.");

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} {arguments.FirstOrDefault()} failed with exit code {process.ExitCode}.");
        }
    }

    /// <summary>
    /// Returns the value of a parameter that a step needs, and throws if it isn't set.
    /// </summary>
    static string Required(string? value, string option) =>
        value is { Length: > 0 }
            ? value
            : throw new InvalidOperationException($"The parameter --{option} isn't set. Pass it on the command line or as the environment variable {option.Replace('-', '_').ToUpperInvariant()}.");

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

    /// <summary>
    /// Returns whether the changelog has a comparison link for <paramref name="version"/>, such as
    /// <c>[1.2.3]: https://github.com/...</c>.
    /// </summary>
    static bool HasVersionLink(string version) =>
        Regex.IsMatch(File.ReadAllText(ChangelogPath), $@"^\[{Regex.Escape(version)}\]:\s*\S+", RegexOptions.Multiline);
}
