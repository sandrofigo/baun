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
    public async Task Logs_the_command_as_information()
    {
        var result = await RunAsync(new ToolBuild(), "Version", "--verbosity", "information");

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(new Regex(@"^\d\d:\d\d:\d\d INF › dotnet --version\s*$", RegexOptions.Multiline), result.Output);
    }

    [Fact]
    public async Task Logs_the_command_after_a_greater_than_sign_where_the_console_lacks_unicode()
    {
        var console = new TestConsole().Width(250);
        console.Profile.Capabilities.Unicode = false;

        var exitCode = await new BuildRunner(new ToolBuild(), console, ParameterSources.None, CIProviderType.None)
            .RunAsync(["Version"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("INF > dotnet --version", console.Output);
    }

    [Fact]
    public async Task Logs_secrets_as_asterisks_in_the_command_and_the_output()
    {
        var build = new ToolBuild();

        var result = await RunAsync(build, "Secret");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("INF › dotnet ***", result.Output);
        Assert.Contains(
            build.Result!.StandardOutput.Concat(build.Result.StandardError),
            line => line.Contains(ToolBuild.SecretValue));
        Assert.DoesNotContain(ToolBuild.SecretValue, result.Output);
    }

    [Fact]
    public void Writes_the_command_with_quotes_around_arguments_with_spaces_and_secrets_as_asterisks()
    {
        ToolCommand command = Tool.Command("dotnet")
            .Arguments("pack", "C:/My Projects/App.slnx", "")
            .Arguments("--api-key").Secret("key with spaces");

        Assert.Equal("""dotnet pack "C:/My Projects/App.slnx" "" --api-key ***""", command.ToString());
    }

    [Fact]
    public void Leaves_the_command_it_adds_to_unchanged()
    {
        ToolCommand command = Tool.Command("git").Arguments("status");

        ToolCommand other = command.Arguments("--short").Secret("token");

        Assert.Equal("git status", command.ToString());
        Assert.Equal("git status --short ***", other.ToString());
    }

    [Fact]
    public async Task Writes_the_output_with_the_logger_of_the_command()
    {
        var result = await RunAsync(new ToolBuild(), "CustomLogger");

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(new Regex(@"^\d\d:\d\d:\d\d ERR v\d+\.\d+\.\d+", RegexOptions.Multiline), result.Output);
    }

    [Fact]
    public void Rejects_a_null_logger()
    {
        Assert.Throws<ArgumentNullException>(() => Tool.Command("dotnet").Logger(null!));
        Assert.Throws<ArgumentNullException>(() => Tool.Logger = null!);
    }

    [Fact]
    public void Rejects_an_empty_secret()
    {
        Assert.Throws<ArgumentException>(() => Tool.Command("dotnet").Secret(""));
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
    public async Task Returns_the_result_for_an_exit_code_other_than_0_with_allow_failure()
    {
        var build = new ToolBuild();

        var result = await RunAsync(build, "AllowedFailure");

        Assert.Equal(0, result.ExitCode);
        Assert.NotEqual(0, build.Result!.ExitCode);
        Assert.Matches(new Regex(@"^\d\d:\d\d:\d\d WRN \S", RegexOptions.Multiline), result.Output);
        Assert.DoesNotContain("failed with exit code", result.Output);
    }

    [Fact]
    public void Leaves_the_command_it_allows_to_fail_unchanged()
    {
        ToolCommand command = Tool.Command("git").Arguments("diff", "--quiet");

        ToolCommand other = command.AllowFailure();

        Assert.NotSame(command, other);
        Assert.Equal(command.ToString(), other.ToString());
    }

    [Fact]
    public async Task Fails_the_step_with_the_name_of_a_tool_that_can_not_be_started()
    {
        var result = await RunAsync(new ToolBuild(), "MissingTool");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ERR Step MissingTool failed", result.Output);
        Assert.Contains($"Couldn't start {ToolBuild.MissingToolName}. Make sure it's installed and in the PATH.", result.Output);
    }

    [Fact]
    public async Task Throws_when_the_working_directory_does_not_exist()
    {
        AbsolutePath directory = (AbsolutePath)Path.GetTempPath() / Path.GetRandomFileName();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Tool.Command("dotnet").Arguments("--version").WorkingDirectory(directory).RunAsync(TestContext.Current.CancellationToken));

        Assert.Equal($"Couldn't start dotnet, because the working directory {directory} doesn't exist.", exception.Message);
    }

    [Fact]
    public async Task Throws_when_the_file_of_a_tool_given_as_a_path_does_not_exist()
    {
        AbsolutePath file = (AbsolutePath)Path.GetTempPath() / Path.GetRandomFileName() / "tool";

        var exception = await RunOutsideABuildAsync(file);

        Assert.Equal($"Couldn't start {file}, because the file doesn't exist.", exception.Message);
    }

    [Fact]
    public async Task Throws_with_the_reason_of_the_system_when_a_tool_can_not_be_started()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Needs a file without the permission to execute it, as on Linux and macOS.");
            return; // Tells the platform analyzer that the rest only runs on Linux and macOS.
        }

        AbsolutePath file = (AbsolutePath)Path.GetTempPath() / Path.GetRandomFileName();
        await File.WriteAllTextAsync(file, "#!/bin/sh\n", TestContext.Current.CancellationToken);
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        try
        {
            var exception = await RunOutsideABuildAsync(file);

            Assert.Equal($"Couldn't start {file}: Permission denied.", exception.Message);
        }
        finally
        {
            file.Delete();
        }
    }

    private static async Task<InvalidOperationException> RunOutsideABuildAsync(string fileName)
    {
        // Outside a build, messages go to the real console, where they would end up in the output of dotnet test.
        using var backend = SerilogBackend.ForConsole(new TestConsole(), LogLevel.Debug);
        using IDisposable scope = Log.Use(backend);

        return await Assert.ThrowsAsync<InvalidOperationException>(
            () => Tool.Command(fileName).RunAsync(TestContext.Current.CancellationToken));
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

        var exitCode = await new BuildRunner(build, console, ParameterSources.None, CIProviderType.None).RunAsync(arguments);

        return (exitCode, console.Output);
    }
}

[Entry(nameof(Version))]
internal sealed partial class ToolBuild
{
    public ToolResult? Result { get; private set; }

    public Step Version() => _ => _
        .Executes(async () => Result = await Tool.RunAsync("dotnet", "--version"));

    public const string SecretValue = "baun-test-secret-4f2a";

    // dotnet writes the name of an unknown command, so the secret is in the output too.
    public Step Secret() => _ => _
        .Executes(async () =>
        {
            try
            {
                await Tool.Command("dotnet").Secret(SecretValue).RunAsync();
            }
            catch (ToolException exception)
            {
                Result = exception.Result;
                throw;
            }
        });

    public Step Unknown() => _ => _
        .Executes(async () => Result = await Tool.RunAsync("dotnet", "no-such-command-for-baun"));

    public Step AllowedFailure() => _ => _
        .Executes(async () => Result = await Tool.Command("dotnet").Arguments("no-such-command-for-baun").AllowFailure().RunAsync());

    public const string MissingToolName = "baun-no-such-tool-4f2a";

    public Step MissingTool() => _ => _
        .Executes(async () => Result = await Tool.RunAsync(MissingToolName));

    public Step CustomLogger() => _ => _
        .Executes(async () => Result = await Tool.Command("dotnet").Arguments("--version").Logger(new PrefixLogger()).RunAsync());

    // Writes each line as an error with a "v" in front, to show that a logger picks the level and can change the line.
    private sealed class PrefixLogger : ToolLogger
    {
        protected internal override LogLevel GetLevel(ToolOutputLine line) => LogLevel.Error;

        protected internal override void Write(ToolOutputLine line) => WriteLine(GetLevel(line), "v" + line.Text);
    }
}
