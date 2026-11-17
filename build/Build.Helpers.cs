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
