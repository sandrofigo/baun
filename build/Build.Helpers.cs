using System.Diagnostics;
using System.Text;
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

    static string GetRequiredEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"The environment variable {name} isn't set.");

    /// <summary>
    /// Renders the entries of a changelog release as Markdown, grouped under their category headings.
    /// </summary>
    static string RenderReleaseNotes(Release release)
    {
        var notes = new StringBuilder();

        foreach (Category category in release.Categories.Where(category => category.Entries.Count > 0))
        {
            if (notes.Length > 0)
            {
                notes.Append('\n');
            }

            notes.Append($"### {category.Type}\n\n");

            foreach (Entry entry in category.Entries)
            {
                string text = entry.Text.ReplaceLineEndings("\n").Replace("\n", "\n  ");
                notes.Append($"- {text}\n");
            }
        }

        if (notes.Length == 0)
        {
            throw new InvalidOperationException($"The section [{release.Version}] in {ChangelogFile} has no entries.");
        }

        return notes.ToString();
    }

    /// <summary>
    /// Creates the GitHub release for <paramref name="tag"/>, or updates its notes if it already exists, so the step
    /// can be run again.
    /// </summary>
    static async Task CreateOrUpdateGitHubRelease(string tag, string notes, bool prerelease)
    {
        string token = GetRequiredEnvironmentVariable("GITHUB_TOKEN");
        string[] repository = GetRequiredEnvironmentVariable("GITHUB_REPOSITORY").Split('/');
        (string owner, string name) = (repository[0], repository[1]);

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
