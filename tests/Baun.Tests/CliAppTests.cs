using System.Diagnostics;
using Baun.Cli;
using Spectre.Console.Testing;

namespace Baun.Tests;

public sealed class CliAppTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("baun-cli-tests-").FullName;
    private readonly TestConsole console = new TestConsole().Width(250);
    private readonly List<ProcessStartInfo> startedProcesses = [];
    private int processExitCode;

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
        Write("build/Build.cs", "await BaunBuild.RunAsync<Build>();\n");
        Write("build/Build.csproj", "<Project />");

        await RunAsync();

        var process = Assert.Single(startedProcesses);
        Assert.Equal(["run", "--project", Path.Combine(root, "build", "Build.csproj"), "--"], process.ArgumentList);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("--version")]
    public async Task Passes_cli_options_to_an_existing_build_so_it_can_use_them(string argument)
    {
        Write("Build.cs", "#:package Baun@0.1.0\n");

        var exitCode = await RunAsync(argument);

        Assert.Equal(0, exitCode);
        Assert.Equal(argument, Assert.Single(startedProcesses).ArgumentList[^1]);
        Assert.Equal(["Build.cs"], Directory.GetFileSystemEntries(root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Passes_the_step_option_to_an_existing_build()
    {
        Write("Build.cs", "#:package Baun@0.1.0\n");

        await RunAsync("--step", "Init");

        Assert.Equal(["--step", "Init"], Assert.Single(startedProcesses).ArgumentList.Skip(4));
    }

    [Theory]
    [InlineData("init")]
    [InlineData("Init")]
    public async Task Init_in_an_existing_build_creates_missing_bootstrap_scripts_and_leaves_the_build_alone(string command)
    {
        Write("Build.cs", "#:package Baun@0.1.0\n");

        var exitCode = await RunAsync(command);

        Assert.Equal(0, exitCode);
        Assert.Empty(startedProcesses);
        Assert.Equal("#:package Baun@0.1.0\n", File.ReadAllText(Path.Combine(root, "Build.cs")));
        Assert.Contains("Created build.sh", console.Output);
        Assert.Equal(BootstrapScripts.Create(root)[Path.Combine(root, "build.ps1")], File.ReadAllText(Path.Combine(root, "build.ps1")));
        Assert.Equal(".dotnet/\n", File.ReadAllText(Path.Combine(root, ".gitignore")));
    }

    [Fact]
    public async Task Init_in_an_existing_project_build_overwrites_the_bootstrap_scripts()
    {
        Write("build/Build.cs", "// my build\n");
        Write("build/Build.csproj", "<Project />");
        Write("build.sh", "# old script\n");
        Directory.CreateDirectory(Path.Combine(root, ".git"));

        var exitCode = await RunAsync("init");

        Assert.Equal(0, exitCode);
        Assert.Equal(BootstrapScripts.Create(root)[Path.Combine(root, "build.sh")], File.ReadAllText(Path.Combine(root, "build.sh")));
        Assert.Contains("Updated build.sh", console.Output);
        Assert.Contains("Created build.ps1", console.Output);
        Assert.Equal("// my build\n", File.ReadAllText(Path.Combine(root, "build", "Build.cs")));
        Assert.Equal(["add", "--chmod=+x", "--", "build.sh"], Assert.Single(startedProcesses).ArgumentList);
    }

    [Theory]
    [InlineData("--single-file")]
    [InlineData("--project")]
    [InlineData("--no-bootstrap")]
    public async Task Init_in_an_existing_build_rejects_build_options(string option)
    {
        Write("Build.cs", "#:package Baun@0.1.0\n");

        var exitCode = await RunAsync("init", option);

        Assert.Equal(1, exitCode);
        Assert.Contains("A build already exists (Build.cs).", console.Output);
        Assert.Equal(["Build.cs"], Directory.GetFileSystemEntries(root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Init_in_an_existing_build_fails_when_the_scripts_could_not_find_it()
    {
        Write("build/Build.cs", "#:package Baun@0.1.0\n");

        var exitCode = await RunAsync("init");

        Assert.Equal(1, exitCode);
        Assert.Contains("but the build is " + Path.Combine("build", "Build.cs"), console.Output);
        Assert.False(File.Exists(Path.Combine(root, "build.sh")));
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
        Assert.DoesNotContain("RunAsync", content); // the source generator adds the entry point
        Assert.Contains("[Entry(nameof(Test))]\npartial class Build", content);
        Assert.Equal(new BuildLocation(buildFile, null), BuildLocator.Find(root));

        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(buildFile).HasFlag(UnixFileMode.UserExecute));
        }
    }

    [Theory]
    [InlineData("Init", "--project")]
    [InlineData("INIT", "--Project")]
    public async Task Init_command_and_options_are_case_insensitive(string command, string option)
    {
        var exitCode = await RunAsync(command, option, "--no-bootstrap");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(root, "build", "Build.csproj")));
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
        Assert.Contains("<RootNamespace></RootNamespace>", File.ReadAllText(projectFile));
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

    [Fact]
    public async Task Init_creates_bootstrap_scripts_by_default()
    {
        var exitCode = await RunAsync("init", "--single-file");

        Assert.Equal(0, exitCode);

        var shellScript = File.ReadAllText(Path.Combine(root, "build.sh"));
        Assert.StartsWith("#!/usr/bin/env sh\n", shellScript);
        Assert.Contains("https://dot.net/v1/dotnet-install.sh", shellScript);
        Assert.Contains("--channel \"$DOTNET_CHANNEL\"", shellScript);
        Assert.Contains("https://dot.net/v1/dotnet-install.ps1", File.ReadAllText(Path.Combine(root, "build.ps1")));
        Assert.Contains("%~dp0build.ps1", File.ReadAllText(Path.Combine(root, "build.cmd")));
        Assert.Equal(".dotnet/\n", File.ReadAllText(Path.Combine(root, ".gitignore")));

        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(Path.Combine(root, "build.sh")).HasFlag(UnixFileMode.UserExecute));
        }
    }

    [Fact]
    public async Task Init_skips_the_bootstrap_scripts_with_no_bootstrap()
    {
        var exitCode = await RunAsync("init", "--project", "--no-bootstrap");

        Assert.Equal(0, exitCode);
        Assert.Equal(["build"], Directory.GetFileSystemEntries(root).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("bin/\nobj/", "bin/\nobj/\n.dotnet/\n")]
    [InlineData("bin/\n", "bin/\n.dotnet/\n")]
    [InlineData("bin/\n/.dotnet\n", "bin/\n/.dotnet\n")]
    [InlineData(".dotnet/\n", ".dotnet/\n")]
    public async Task Init_adds_the_dotnet_folder_to_an_existing_gitignore(string before, string after)
    {
        Write(".gitignore", before);

        await RunAsync("init", "--single-file");

        Assert.Equal(after, File.ReadAllText(Path.Combine(root, ".gitignore")));
    }

    [Theory]
    [InlineData("--single-file", new[] { "Build.cs", "build.sh" })]
    [InlineData("--project", new[] { "build.sh" })]
    public async Task Init_stages_executable_files_in_a_git_repository(string kind, string[] executables)
    {
        Directory.CreateDirectory(Path.Combine(root, ".git"));

        var exitCode = await RunAsync("init", kind);

        Assert.Equal(0, exitCode);
        var git = Assert.Single(startedProcesses);
        Assert.Equal("git", git.FileName);
        Assert.Equal(root, git.WorkingDirectory);
        Assert.Equal(["add", "--chmod=+x", "--", .. executables], git.ArgumentList);
        Assert.Contains("as executable in git", console.Output);
    }

    [Fact]
    public async Task Init_does_not_run_git_outside_a_git_repository()
    {
        await RunAsync("init", "--single-file");

        Assert.Empty(startedProcesses);
    }

    [Fact]
    public async Task Init_warns_when_git_fails_but_still_succeeds()
    {
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        processExitCode = 128;

        var exitCode = await RunAsync("init", "--single-file");

        Assert.Equal(0, exitCode);
        Assert.Contains("warning: Couldn't mark Build.cs, build.sh as executable in git.", console.Output);
        Assert.Contains("Run git add --chmod=+x -- Build.cs build.sh before committing.", console.Output);
    }

    [Fact]
    public async Task Init_overwrites_an_existing_bootstrap_script()
    {
        Write("build.sh", "# my own script\n");

        var exitCode = await RunAsync("init", "--single-file");

        Assert.Equal(0, exitCode);
        Assert.Contains("Updated build.sh", console.Output);
        Assert.Equal(BootstrapScripts.Create(root)[Path.Combine(root, "build.sh")], File.ReadAllText(Path.Combine(root, "build.sh")));
        Assert.True(File.Exists(Path.Combine(root, "Build.cs")));
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
            return Task.FromResult(processExitCode);
        });

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
