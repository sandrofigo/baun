using System.Diagnostics;
using KeepAChangelog.IO;
using GitHub = Octokit;

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
    /// Gets the categories and entries of <paramref name="release"/> without its heading, which the GitHub release
    /// already shows as its title.
    /// </summary>
    static string GetReleaseNotes(Release release) =>
        string.Join(
            "\n\n",
            release.Categories
                .Where(category => category.Entries.Count > 0)
                .OrderBy(category => category.Type)
                .Select(category => category.ToString()));

    /// <summary>
    /// Creates the GitHub release for <paramref name="tag"/> in <paramref name="repository"/> (owner/name), or updates
    /// its notes if it already exists, so the step can be run again.
    /// </summary>
    static async Task CreateOrUpdateGitHubRelease(string token, string repository, string tag, string notes, bool prerelease)
    {
        string[] parts = repository.Split('/');
        (string owner, string name) = (parts[0], parts[1]);

        var client = new GitHub.GitHubClient(new GitHub.ProductHeaderValue("baun-build"))
        {
            Credentials = new GitHub.Credentials(token)
        };

        GitHub.Release? existing;

        try
        {
            existing = await client.Repository.Release.Get(owner, name, tag);
        }
        catch (GitHub.NotFoundException)
        {
            existing = null;
        }

        if (existing is null)
        {
            GitHub.Release created = await client.Repository.Release.Create(owner, name, new GitHub.NewRelease(tag)
            {
                Name = tag,
                Body = notes,
                Prerelease = prerelease,
            });

            Console.WriteLine($"Created GitHub release {created.HtmlUrl}");
        }
        else
        {
            GitHub.Release updated = await client.Repository.Release.Edit(owner, name, existing.Id, new GitHub.ReleaseUpdate
            {
                Name = tag,
                Body = notes,
                Prerelease = prerelease,
            });

            Console.WriteLine($"Updated GitHub release {updated.HtmlUrl}");
        }
    }
}
