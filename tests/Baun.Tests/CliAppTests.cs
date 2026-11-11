using System.Diagnostics;
using BaunCli;

namespace BaunTests;

public sealed class CliAppTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("baun-cli-tests-").FullName;
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();
    private readonly List<ProcessStartInfo> startedProcesses = [];

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public async Task Passes_all_arguments_to_an_existing_build()
    {
        Write("Build.cs", "#:package Baun@0.1.0\n");

        var exitCode = await RunAsync(input: "", "Compile", "Test");

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

        await RunAsync(input: "");

        var process = Assert.Single(startedProcesses);
        Assert.Equal(["run", "--project", Path.Combine(root, "build", "Build.csproj"), "--"], process.ArgumentList);
    }

    [Fact]
    public async Task Passes_init_to_an_existing_build_so_it_can_run_a_Init_step()
    {
        Write("Build.cs", "#:package Baun@0.1.0\n");

        var exitCode = await RunAsync(input: "", "init");

        Assert.Equal(0, exitCode);
        Assert.Equal("init", Assert.Single(startedProcesses).ArgumentList[^1]);
        Assert.Equal(["Build.cs"], Directory.GetFileSystemEntries(root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Fails_without_a_build_and_suggests_init()
    {
        var exitCode = await RunAsync(input: "", "Test");

        Assert.Equal(1, exitCode);
        Assert.Empty(startedProcesses);
        Assert.Contains("Run 'baun init' to create one.", error.ToString());
    }

    [Fact]
    public async Task Init_creates_a_single_file_build()
    {
        var exitCode = await RunAsync(input: "", "init", "--single-file");

        Assert.Equal(0, exitCode);
        Assert.Empty(startedProcesses);

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
        var exitCode = await RunAsync(input: "", "init", "--project");

        Assert.Equal(0, exitCode);

        var buildFile = Path.Combine(root, "build", "Build.cs");
        var projectFile = Path.Combine(root, "build", "Build.csproj");
        Assert.DoesNotContain("#:", File.ReadAllText(buildFile));
        Assert.Contains("<PackageReference Include=\"Baun\" Version=\"", File.ReadAllText(projectFile));
        Assert.Equal(new BuildLocation(buildFile, projectFile), BuildLocator.Find(root));
    }

    [Theory]
    [InlineData("", "Build.cs")]
    [InlineData("1", "Build.cs")]
    [InlineData("3\n2", "build/Build.csproj")]
    public async Task Init_asks_which_kind_of_build_to_create(string input, string expectedFile)
    {
        var exitCode = await RunAsync(input, interactive: true, "init");

        Assert.Equal(0, exitCode);
        Assert.Contains("Which kind of build do you want to create?", output.ToString());
        Assert.True(File.Exists(Path.Combine(root, expectedFile)));
    }

    [Fact]
    public async Task Init_creates_a_single_file_build_when_it_cannot_ask()
    {
        var exitCode = await RunAsync(input: "", "init");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("Which kind of build", output.ToString());
        Assert.True(File.Exists(Path.Combine(root, "Build.cs")));
    }

    [Fact]
    public async Task Init_does_not_overwrite_existing_files()
    {
        Write("build/Build.cs", "// my own file\n");

        var exitCode = await RunAsync(input: "", "init", "--project");

        Assert.Equal(1, exitCode);
        Assert.Contains("already exists", error.ToString());
        Assert.Equal("// my own file\n", File.ReadAllText(Path.Combine(root, "build", "Build.cs")));
        Assert.False(File.Exists(Path.Combine(root, "build", "Build.csproj")));
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--single-file", "--project")]
    public async Task Init_fails_for_invalid_options(params string[] options)
    {
        var exitCode = await RunAsync(input: "", ["init", .. options]);

        Assert.Equal(1, exitCode);
        Assert.StartsWith("error:", error.ToString());
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    private Task<int> RunAsync(string input, params string[] arguments) =>
        RunAsync(input, interactive: false, arguments);

    private Task<int> RunAsync(string input, bool interactive, params string[] arguments)
    {
        var console = new CliConsole(new StringReader(input), output, error, interactive);

        return CliApp.RunAsync(arguments, root, console, startInfo =>
        {
            startedProcesses.Add(startInfo);
            return Task.FromResult(0);
        });
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
