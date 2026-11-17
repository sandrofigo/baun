using System.Text.RegularExpressions;
using Baun.CI;
using Baun.Logging;
using Spectre.Console.Testing;

namespace Baun.Tests;

public class ToolTests
{
    [Fact]
    public async Task Logs_the_standard_output_as_debug_and_returns_it()
    {
        var build = new ToolBuild();

        var result = await RunAsync(build, "Version");

        Assert.Equal(0, result.ExitCode);
        string version = Assert.Single(build.Result!.StandardOutput);
        Assert.Matches(@"^\d+\.\d+\.\d+", version);
        Assert.Matches(new Regex($@"^\d\d:\d\d:\d\d DBG {Regex.Escape(version)}\s*$", RegexOptions.Multiline), result.Output);
    }

    [Fact]
    public async Task Hides_the_standard_output_with_verbosity_information()
    {
        var build = new ToolBuild();

        var result = await RunAsync(build, "Version", "--verbosity", "information");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("DBG", result.Output);
        Assert.Single(build.Result!.StandardOutput);
    }

    [Fact]
    public async Task Logs_the_standard_error_as_warnings_and_fails_the_step_for_an_exit_code_other_than_0()
    {
        var build = new ToolBuild();

        var result = await RunAsync(build, "Unknown");

        Assert.Equal(1, result.ExitCode);
        Assert.Matches(new Regex(@"^\d\d:\d\d:\d\d WRN \S", RegexOptions.Multiline), result.Output);
        Assert.Contains("ERR Step Unknown failed", result.Output);
        Assert.Contains("dotnet failed with exit code", result.Output);
    }

    [Fact]
    public async Task Runs_in_the_working_directory()
    {
        AbsolutePath directory = (AbsolutePath)Path.GetTempPath() / Path.GetRandomFileName();
        directory.CreateDirectory();

        // Outside a build, messages go to the real console, where they would end up in the output of dotnet test.
        using var backend = SerilogBackend.ForConsole(new TestConsole(), LogLevel.Debug);
        using IDisposable scope = Log.Use(backend);

        try
        {
            ToolResult result = await Tool.RunAsync(
                "dotnet",
                ["new", "globaljson", "--sdk-version", "10.0.100"],
                directory,
                TestContext.Current.CancellationToken);

            Assert.Equal(0, result.ExitCode);
            Assert.True((directory / "global.json").FileExists);
        }
        finally
        {
            directory.Delete();
        }
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(IBaunBuild build, params string[] arguments)
    {
        var console = new TestConsole().Width(250);

        var exitCode = await new BuildRunner(build, console, ParameterSources.None, CIProvider.None).RunAsync(arguments);

        return (exitCode, console.Output);
    }
}

[Entry(nameof(Version))]
internal sealed partial class ToolBuild
{
    public ToolResult? Result { get; private set; }

    public Step Version() => _ => _
        .Executes(async () => Result = await Tool.RunAsync("dotnet", "--version"));

    public Step Unknown() => _ => _
        .Executes(async () => Result = await Tool.RunAsync("dotnet", "no-such-command-for-baun"));
}
