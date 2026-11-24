using System.Text.RegularExpressions;
using Baun.CI;
using Baun.Logging;
using Baun.Tools.DotNet;
using Spectre.Console.Testing;

namespace Baun.Tests;

public class DotNetTests
{
    [Fact]
    public void Builds_typed_commands_with_their_options()
    {
        Assert.Equal(
            "dotnet pack App.slnx --configuration Release --output \"C:/My Artifacts\" -p:Version=1.2.3 -p:ContinuousIntegrationBuild=true",
            DotNet.Pack("App.slnx")
                .Configuration("Release")
                .Output("C:/My Artifacts")
                .Version("1.2.3")
                .ContinuousIntegrationBuild()
                .ToString());

        Assert.Equal("dotnet build -p:Version=1.2.3 -p:ContinuousIntegrationBuild=false -p:DebugType=embedded", DotNet.Build().Version("1.2.3").ContinuousIntegrationBuild(false).Property("DebugType", "embedded").ToString());

        Assert.Equal(
            "dotnet test --solution App.slnx --configuration Debug --no-build --verbosity minimal",
            DotNet.Test().Solution("App.slnx").Configuration("Debug").NoBuild().Verbosity(DotNetVerbosity.Minimal).ToString());

        Assert.Equal("dotnet build --framework net10.0 --runtime linux-x64 --no-restore", DotNet.Build().Framework("net10.0").Runtime("linux-x64").NoRestore().ToString());
        Assert.Equal("dotnet publish App.csproj --no-self-contained", DotNet.Publish("App.csproj").SelfContained(false).ToString());
        Assert.Equal("dotnet restore", DotNet.Restore().ToString());
        Assert.Equal("dotnet clean App.slnx", DotNet.Clean("App.slnx").ToString());
        Assert.Equal(
            "dotnet clean --configuration Release --framework net10.0 --output out --runtime linux-x64",
            DotNet.Clean().Configuration("Release").Framework("net10.0").Output("out").Runtime("linux-x64").ToString());
        Assert.Equal("dotnet restore --runtime linux-x64 --verbosity quiet", DotNet.Restore().Runtime("linux-x64").Verbosity(DotNetVerbosity.Quiet).ToString());
    }

    [Fact]
    public void Takes_more_arguments_and_secrets_after_typed_options()
    {
        DotNetNuGetPushCommand push = DotNet.NuGetPush("App.1.0.0.nupkg")
            .ApiKey("key")
            .Source("https://api.nuget.org/v3/index.json")
            .SkipDuplicate()
            .Arguments("--timeout", "600")
            .Secret("other");

        Assert.Equal(
            "dotnet nuget push App.1.0.0.nupkg --api-key *** --source https://api.nuget.org/v3/index.json --skip-duplicate --timeout 600 ***",
            push.ToString());
    }

    [Fact]
    public void Leaves_the_command_it_adds_to_unchanged()
    {
        DotNetBuildCommand build = DotNet.Build("App.slnx");

        DotNetBuildCommand release = build.Configuration("Release");

        Assert.Equal("dotnet build App.slnx", build.ToString());
        Assert.Equal("dotnet build App.slnx --configuration Release", release.ToString());
    }

    [Theory]
    [InlineData(@"C:\src\Program.cs(3,1): error CS1002: ; expected [C:\src\App.csproj]", LogLevel.Error)]
    [InlineData(@"C:\Program Files\dotnet\sdk\10.0.100\Microsoft.NET.Sdk.targets(10,5): warning NETSDK1138: The target framework is out of support.", LogLevel.Warning)]
    [InlineData("error: Response status code does not indicate success: 403 (Forbidden).", LogLevel.Error)]
    [InlineData("warn : The package already exists.", LogLevel.Warning)]
    [InlineData("    0 Warning(s)", LogLevel.Debug)]
    [InlineData("Build succeeded with 2 warning(s) in 3.1s", LogLevel.Debug)]
    [InlineData("  App -> C:\\src\\bin\\Release\\net10.0\\App.dll", LogLevel.Debug)]
    public void Logs_errors_and_warnings_of_msbuild_and_nuget_as_such(string text, LogLevel level)
    {
        Assert.Equal(level, new DotNetLogger().GetLevel(new ToolOutputLine(ToolStream.StandardOutput, text)));
    }

    [Fact]
    public void Logs_other_lines_of_the_standard_error_as_warnings()
    {
        Assert.Equal(LogLevel.Warning, new DotNetLogger().GetLevel(new ToolOutputLine(ToolStream.StandardError, "Something")));
    }

    [Fact]
    public async Task Writes_the_output_with_a_logger_derived_from_the_dotnet_logger()
    {
        var result = await RunAsync(new DotNetBuild(() => DotNet.Command("--version").Logger(new VersionLogger())));

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(new Regex(@"^\d\d:\d\d:\d\d INF \d+\.\d+\.\d+", RegexOptions.Multiline), result.Output);
    }

    [Fact]
    public async Task Writes_the_output_of_all_commands_with_the_logger_set_for_dotnet()
    {
        ToolLogger previous = DotNet.Logger;
        DotNet.Logger = new VersionLogger();

        try
        {
            var result = await RunAsync(new DotNetBuild(() => DotNet.Command("--version")));

            Assert.Equal(0, result.ExitCode);
            Assert.Matches(new Regex(@"^\d\d:\d\d:\d\d INF \d+\.\d+\.\d+", RegexOptions.Multiline), result.Output);
        }
        finally
        {
            DotNet.Logger = previous;
        }
    }

    [Fact]
    public async Task Allows_a_typed_command_to_fail()
    {
        var result = await RunAsync(new DotNetBuild(() => DotNet.Build("no-such-project-for-baun.csproj").AllowFailure().Command));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("MSB1009", result.Output);
    }

    [Fact]
    public void Rejects_a_null_logger()
    {
        Assert.Throws<ArgumentNullException>(() => DotNet.Logger = null!);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(IBaunBuild build)
    {
        var console = new TestConsole().Width(250);

        var exitCode = await new BuildRunner(build, console, ParameterSources.None, CIProvider.None).RunAsync(["Run"]);

        return (exitCode, console.Output);
    }

    // Logs the version that dotnet --version writes as information, and leaves other lines to the dotnet logger.
    private sealed class VersionLogger : DotNetLogger
    {
        protected internal override LogLevel GetLevel(ToolOutputLine line) =>
            Regex.IsMatch(line.Text, @"^\d+\.\d+\.\d+") ? LogLevel.Information : base.GetLevel(line);
    }
}

[Entry(nameof(Run))]
internal sealed partial class DotNetBuild(Func<ToolCommand> command)
{
    public Step Run() => _ => _
        .Executes(async () => await command().RunAsync());
}
