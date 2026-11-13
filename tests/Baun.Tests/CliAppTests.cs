using System.Diagnostics;
using BaunCli;
using Spectre.Console.Testing;

namespace BaunTests;

public sealed class CliAppTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("baun-cli-tests-").FullName;
    private readonly TestConsole console = new TestConsole().Width(250);
    private readonly List<ProcessStartInfo> startedProcesses = [];

    public void Dispose()
    {
        console.Dispose();
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task Passes_all_arguments_to_an_existing_build()
    {
        Write("Build.cs", "#:package Baun@0.1.0\n");

        var exitCode = await RunAsync("Compile", "Test");

        Assert.Equal(0, exitCode);
        var process = Assert.Single(startedProcesses);
        Assert.Equal("dotnet", process.FileName);
        Assert.Equal(["run", "--file", Path.Combine(root, "Build.cs"), "--", "Compile", "Test"], process.ArgumentList);
    }

    [Fact]
    public async Task Runs_a_project_build_with_its_project_file()
    {
        Write("build/Build.cs", "await Baun.RunAsync<Build>();\n");
        Write("build/Build.csproj", "<Project />");

        await RunAsync();

        var process = Assert.Single(startedProcesses);
        Assert.Equal(["run", "--project", Path.Combine(root, "build", "Build.csproj"), "--"], process.ArgumentList);
    }

    [Theory]
    [InlineData("init")]
    [InlineData("--help")]
    [InlineData("--version")]
    public async Task Passes_cli_commands_to_an_existing_build_so_it_can_use_them(string argument)
    {
        Write("Build.cs", "#:package Baun@0.1.0\n");

        var exitCode = await RunAsync(argument);

        Assert.Equal(0, exitCode);
        Assert.Equal(argument, Assert.Single(startedProcesses).ArgumentList[^1]);
        Assert.Equal(["Build.cs"], Directory.GetFileSystemEntries(root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Fails_without_a_build_and_suggests_init()
    {
        var exitCode = await RunAsync("Test");

        Assert.Equal(1, exitCode);
        Assert.Empty(startedProcesses);
        Assert.Contains("No baun build found.", console.Output);
        Assert.Contains("Run baun init to create one.", console.Output);
    }

    [Fact]
    public async Task Shows_the_init_command_in_the_help_without_a_build()
    {
        var exitCode = await RunAsync("--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("init", console.Output);
        Assert.Contains("Creates a new build in the current folder", console.Output);
    }

    [Fact]
    public async Task Init_creates_a_single_file_build()
    {
        var exitCode = await RunAsync("init", "--single-file");

        Assert.Equal(0, exitCode);
        Assert.Empty(startedProcesses);
        Assert.Contains("Created Build.cs", console.Output);

        var buildFile = Path.Combine(root, "Build.cs");
        var content = File.ReadAllText(buildFile);
        Assert.StartsWith("#!/usr/bin/env dotnet\n#:package Baun@", content);
        Assert.Contains("await Baun.RunAsync<Build>();", content);
        Assert.Contains("[Entry]", content);
        Assert.Equal(new BuildLocation(buildFile, null), BuildLocator.Find(root));

        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(buildFile).HasFlag(UnixFileMode.UserExecute));
        }
    }

    [Fact]
    public async Task Init_creates_a_project_build()
    {
        var exitCode = await RunAsync("init", "--project");

        Assert.Equal(0, exitCode);

        var buildFile = Path.Combine(root, "build", "Build.cs");
        var projectFile = Path.Combine(root, "build", "Build.csproj");
        Assert.DoesNotContain("#:", File.ReadAllText(buildFile));
        Assert.Contains("<PackageReference Include=\"Baun\" Version=\"", File.ReadAllText(projectFile));
        Assert.Equal(new BuildLocation(buildFile, projectFile), BuildLocator.Find(root));
    }

    [Theory]
    [InlineData(0, "Build.cs")]
    [InlineData(1, "build/Build.csproj")]
    public async Task Init_asks_which_kind_of_build_to_create(int downArrows, string expectedFile)
    {
        console.Interactive();

        for (var i = 0; i < downArrows; i++)
        {
            console.Input.PushKey(ConsoleKey.DownArrow);
        }

        console.Input.PushKey(ConsoleKey.Enter);

        var exitCode = await RunAsync("init");

        Assert.Equal(0, exitCode);
        Assert.Contains("Which kind of build do you want to create?", console.Output);
        Assert.True(File.Exists(Path.Combine(root, expectedFile)));
    }

    [Fact]
    public async Task Init_creates_a_single_file_build_when_it_cannot_ask()
    {
        var exitCode = await RunAsync("init");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("Which kind of build", console.Output);
        Assert.True(File.Exists(Path.Combine(root, "Build.cs")));
    }

    [Fact]
    public async Task Init_does_not_overwrite_existing_files()
    {
        Write("build/Build.cs", "// my own file\n");

        var exitCode = await RunAsync("init", "--project");

        Assert.Equal(1, exitCode);
        Assert.Contains("already exists", console.Output);
        Assert.Equal("// my own file\n", File.ReadAllText(Path.Combine(root, "build", "Build.cs")));
        Assert.False(File.Exists(Path.Combine(root, "build", "Build.csproj")));
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--single-file", "--project")]
    public async Task Init_fails_for_invalid_options(params string[] options)
    {
        var exitCode = await RunAsync(["init", .. options]);

        Assert.NotEqual(0, exitCode);
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    private Task<int> RunAsync(params string[] arguments) =>
        CliApp.RunAsync(arguments, root, console, startInfo =>
        {
            startedProcesses.Add(startInfo);
            return Task.FromResult(0);
        });

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
