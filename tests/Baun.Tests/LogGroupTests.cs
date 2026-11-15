using System.Text.RegularExpressions;
using Baun.CI;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Baun.Tests;

public class LogGroupTests
{
    [Theory]
    [InlineData(nameof(CIProvider.GitHubActions), "::group::Restore", "::endgroup::")]
    [InlineData(nameof(CIProvider.AzurePipelines), "##[group]Restore", "##[endgroup]")]
    [InlineData(nameof(CIProvider.TeamCity), "##teamcity[blockOpened name='Restore']", "##teamcity[blockClosed name='Restore']")]
    public void Writes_the_markers_of_the_CI_server(string provider, string start, string end)
    {
        string[] lines = Write(Enum.Parse<CIProvider>(provider), "Restore");

        Assert.Equal([start, "inside", end], lines);
    }

    [Fact]
    public void Writes_GitLab_sections()
    {
        string[] lines = Write(CIProvider.GitLab, "Run tests");

        Assert.Equal(3, lines.Length);
        Match start = Regex.Match(lines[0], @"^\e\[0Ksection_start:\d+:(run_tests_\d+)\[collapsed=true\]\r\e\[0KRun tests$");
        Assert.True(start.Success, lines[0]);
        Assert.Equal("inside", lines[1]);
        Assert.Matches($@"^\e\[0Ksection_end:\d+:{start.Groups[1].Value}\r\e\[0K$", lines[2]);
    }

    [Fact]
    public void Makes_GitLab_section_ids_unique()
    {
        Assert.NotEqual(LogGroups.GitLabSectionId("Test"), LogGroups.GitLabSectionId("Test"));
    }

    [Fact]
    public void Escapes_TeamCity_names()
    {
        Assert.Equal("a|'b|||[c|]|n|r", LogGroups.EscapeTeamCity("a'b|[c]\n\r"));
    }

    [Fact]
    public void Writes_a_name_with_line_breaks_on_one_line()
    {
        string[] lines = Write(CIProvider.GitHubActions, "first\nsecond");

        Assert.Equal("::group::first second", lines[0]);
    }

    [Fact]
    public void Writes_a_line_instead_of_a_group_outside_CI()
    {
        string[] lines = Write(CIProvider.None, "Restore");

        Assert.Equal(["> Restore", "inside"], lines);
    }

    [Fact]
    public void Writes_a_group_inside_another_group_as_a_line()
    {
        var console = new TestConsole();

        using (LogGroups.Begin(console, CIProvider.GitHubActions, "Outer", "> Outer"))
        {
            using (LogGroups.Begin(console, CIProvider.GitHubActions, "Inner", "> Inner"))
            {
                console.WriteLine("inside");
            }
        }

        Assert.Equal(["::group::Outer", "> Inner", "inside", "::endgroup::"], Lines(console));
    }

    [Fact]
    public void Ends_a_group_only_once()
    {
        var console = new TestConsole();

        IDisposable group = LogGroups.Begin(console, CIProvider.GitHubActions, "Restore", "> Restore");
        group.Dispose();
        group.Dispose();

        Assert.Equal(["::group::Restore", "::endgroup::"], Lines(console));
    }

    [Fact]
    public void Starts_a_new_group_after_a_group_that_ended_with_an_inner_group_still_open()
    {
        var console = new TestConsole();

        using (LogGroups.Begin(console, CIProvider.GitHubActions, "First", "> First"))
        {
            LogGroups.Begin(console, CIProvider.GitHubActions, "Forgotten", "> Forgotten");
        }

        using (LogGroups.Begin(console, CIProvider.GitHubActions, "Second", "> Second"))
        {
        }

        Assert.Equal(["::group::First", "> Forgotten", "::endgroup::", "::group::Second", "::endgroup::"], Lines(console));
    }

    private static string[] Write(CIProvider provider, string name)
    {
        var console = new TestConsole();

        using (LogGroups.Begin(console, provider, name, $"> {name}"))
        {
            console.WriteLine("inside");
        }

        return Lines(console);
    }

    private static string[] Lines(TestConsole console) =>
        console.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToArray();
}
