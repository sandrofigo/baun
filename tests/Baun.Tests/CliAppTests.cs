extern alias cli;

using System.Diagnostics;
using System.Text.RegularExpressions;
using cli::Baun.Cli;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Baun.Tests;

using AbsolutePath = cli::Baun.AbsolutePath;

public sealed class CliAppTests : IDisposable
{
    private readonly AbsolutePath root = (AbsolutePath)Directory.CreateTempSubdirectory("baun-cli-tests-").FullName;
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
        Assert.Equal(["run", "--file", root / "Build.cs", "--", "Compile", "Test"], process.ArgumentList);
    }

    [Fact]
    public async Task Runs_a_project_build_with_its_project_file()
    {
        Write("build/Build.cs", "await BaunBuild.RunAsync<Build>();\n");
        Write("build/_build.csproj", "<Project />");

        await RunAsync();

        var process = Assert.Single(startedProcesses);
        Assert.Equal(["run", "--project", root / "build" / "_build.csproj", "--"], process.ArgumentList);
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
        Assert.Equal("#:package Baun@0.1.0\n", File.ReadAllText(root / "Build.cs"));
        Assert.Contains("Created build.sh", console.Output);
        Assert.Equal(BootstrapScripts.Create(root)[root / "build.ps1"], File.ReadAllText(root / "build.ps1"));
        Assert.Equal(".dotnet/\n", File.ReadAllText(root / ".gitignore"));
    }

    [Fact]
    public async Task Init_in_an_existing_project_build_overwrites_the_bootstrap_scripts()
    {
        Write("build/Build.cs", "// my build\n");
        Write("build/_build.csproj", "<Project />");
        Write("build.sh", "# old script\n");
        Directory.CreateDirectory(root / ".git");

        var exitCode = await RunAsync("init");

        Assert.Equal(0, exitCode);
        Assert.Equal(BootstrapScripts.Create(root)[root / "build.sh"], File.ReadAllText(root / "build.sh"));
        Assert.Contains("Updated build.sh", console.Output);
        Assert.Contains("Created build.ps1", console.Output);
        Assert.Equal("// my build\n", File.ReadAllText(root / "build" / "Build.cs"));
        Assert.Contains("csharp_default_private_modifier = implicit", File.ReadAllText(root / "build" / ".editorconfig"));
        Assert.Contains("DEFAULT_PRIVATE_MODIFIER", File.ReadAllText(root / "build" / "_build.csproj.DotSettings"));
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
        Assert.False(File.Exists(root / "build.sh"));
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
    public async Task Colors_only_the_headings_and_usage_placeholders_of_the_help()
    {
        console.EmitAnsiSequences().Colors(ColorSystem.TrueColor);

        var exitCode = await RunAsync("init", "--help");

        Assert.Equal(0, exitCode);
        var colored = Regex.Matches(console.Output, @"\e\[38;[0-9;]+m([^\e]*)\e\[0m").Select(match => match.Groups[1].Value);
        Assert.Equal(["DESCRIPTION:", "USAGE:", "[OPTIONS]", "EXAMPLES:", "OPTIONS:"], colored);
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

        var buildFile = root / "Build.cs";
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
        Assert.True(File.Exists(root / "build" / "_build.csproj"));
    }

    [Fact]
    public async Task Init_creates_a_project_build()
    {
        var exitCode = await RunAsync("init", "--project");

        Assert.Equal(0, exitCode);

        var buildFile = root / "build" / "Build.cs";
        var projectFile = root / "build" / "_build.csproj";
        Assert.DoesNotContain("#:", File.ReadAllText(buildFile));
        Assert.Contains("<PackageReference Include=\"Baun\" Version=\"", File.ReadAllText(projectFile));
        Assert.Contains("<RootNamespace></RootNamespace>", File.ReadAllText(projectFile));
        Assert.Contains("csharp_default_private_modifier = implicit", File.ReadAllText(root / "build" / ".editorconfig"));
        Assert.Contains("Instance fields (private)", File.ReadAllText(root / "build" / "_build.csproj.DotSettings"));
        Assert.Equal(new BuildLocation(buildFile, projectFile), BuildLocator.Find(root));
    }

    [Fact]
    public async Task Init_writes_the_dot_settings_as_rider_saves_them()
    {
        // Rider rewrites the file when it opens the project unless it's already in the form Rider saves it in.
        await RunAsync("init", "--project");

        byte[] bytes = File.ReadAllBytes(root / "build" / "_build.csproj.DotSettings");
        string text = File.ReadAllText(root / "build" / "_build.csproj.DotSettings");

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Contains("&lt;Policy Inspect=\"True\" WarnAboutPrefixesAndSuffixes=\"False\" Prefix=\"\"", text);
        Assert.Contains("SettingsMigration/IsMigratorApplied/=JetBrains_002EReSharper_002EPsi_002ECSharp_002ECodeStyle_002ESettingsUpgrade_002EPredefinedNamingRulesToUserRulesUpgrade/@EntryIndexedValue\">True</s:Boolean>", text);
        Assert.DoesNotContain('\r', text);
    }

    [Theory]
    [InlineData(0, "build/_build.csproj", "Project")]
    [InlineData(1, "Build.cs", "Single file")]
    public async Task Init_asks_which_kind_of_build_to_create(int downArrows, string expectedFile, string expectedAnswer)
    {
        console.Interactive();

        for (var i = 0; i < downArrows; i++)
        {
            console.Input.PushKey(ConsoleKey.DownArrow);
        }

        console.Input.PushKey(ConsoleKey.Enter);

        var exitCode = await RunAsync("init");

        Assert.Equal(0, exitCode);
        Assert.Contains($"✓ Which kind of build do you want to create? › {expectedAnswer}", console.Output);
        Assert.True(File.Exists(root / expectedFile));
    }

    [Theory]
    [InlineData("My.sln")]
    [InlineData("My.slnx")]
    public async Task Init_adds_a_project_build_to_the_solution_when_confirmed(string solution)
    {
        Write(solution, "");
        console.Interactive();
        console.Input.PushTextWithEnter("y");

        var exitCode = await RunAsync("init", "--project", "--no-bootstrap");

        Assert.Equal(0, exitCode);
        Assert.Contains($"Add {Path.Combine("build", "_build.csproj")} to {solution}?", console.Output);
        var dotnet = Assert.Single(startedProcesses);
        Assert.Equal("dotnet", dotnet.FileName);
        Assert.Equal(root, dotnet.WorkingDirectory);
        Assert.Equal(["sln", solution, "add", "--in-root", Path.Combine("build", "_build.csproj")], dotnet.ArgumentList);
        Assert.Contains($"Added {Path.Combine("build", "_build.csproj")} to {solution}", console.Output);
    }

    [Fact]
    public async Task Init_finds_solutions_in_subfolders()
    {
        Write("src/My.sln", "");
        Write("a/b/c/Deep.slnx", "");
        Write("a/b/c/d/TooDeep.sln", "");
        Write(".hidden/Hidden.sln", "");
        Write("src/bin/Output.sln", "");
        console.Interactive();
        console.Input.PushKey(ConsoleKey.Enter); // the shallowest solution comes first

        var exitCode = await RunAsync("init", "--project", "--no-bootstrap");

        Assert.Equal(0, exitCode);
        Assert.Contains(Path.Combine("src", "My.sln"), console.Output);
        Assert.Contains(Path.Combine("a", "b", "c", "Deep.slnx"), console.Output);
        Assert.DoesNotContain("TooDeep", console.Output);
        Assert.DoesNotContain("Hidden", console.Output);
        Assert.DoesNotContain("Output", console.Output);
        Assert.Equal(["sln", Path.Combine("src", "My.sln"), "add", "--in-root", Path.Combine("build", "_build.csproj")], Assert.Single(startedProcesses).ArgumentList);
    }

    [Fact]
    public async Task Init_does_not_add_a_project_build_to_the_solution_when_declined()
    {
        Write("My.sln", "");
        console.Interactive();
        console.Input.PushTextWithEnter("n");

        var exitCode = await RunAsync("init", "--project", "--no-bootstrap");

        Assert.Equal(0, exitCode);
        Assert.Empty(startedProcesses);
    }

    [Theory]
    [InlineData(1, "B.slnx")]
    [InlineData(2, null)]
    public async Task Init_asks_which_solution_to_add_a_project_build_to(int downArrows, string? expectedSolution)
    {
        Write("A.sln", "");
        Write("B.slnx", "");
        console.Interactive();

        for (var i = 0; i < downArrows; i++)
        {
            console.Input.PushKey(ConsoleKey.DownArrow);
        }

        console.Input.PushKey(ConsoleKey.Enter);

        var exitCode = await RunAsync("init", "--project", "--no-bootstrap");

        Assert.Equal(0, exitCode);
        Assert.Contains("to a solution?", console.Output);

        if (expectedSolution is null)
        {
            Assert.Empty(startedProcesses);
        }
        else
        {
            Assert.Equal(expectedSolution, Assert.Single(startedProcesses).ArgumentList[1]);
        }
    }

    [Fact]
    public async Task Init_does_not_add_a_project_build_to_the_solution_when_it_cannot_ask()
    {
        Write("My.sln", "");

        var exitCode = await RunAsync("init", "--project", "--no-bootstrap");

        Assert.Equal(0, exitCode);
        Assert.Empty(startedProcesses);
        Assert.DoesNotContain("My.sln", console.Output);
    }

    [Fact]
    public async Task Init_warns_when_adding_to_the_solution_fails_but_still_succeeds()
    {
        Write("My.sln", "");
        console.Interactive();
        console.Input.PushTextWithEnter("y");
        processExitCode = 1;

        var exitCode = await RunAsync("init", "--project", "--no-bootstrap");

        Assert.Equal(0, exitCode);
        Assert.Contains($"warning: Couldn't add {Path.Combine("build", "_build.csproj")} to My.sln.", console.Output);
        Assert.Contains($"Run dotnet sln My.sln add --in-root {Path.Combine("build", "_build.csproj")} to add it.", console.Output);
    }

    [Fact]
    public async Task Init_creates_a_project_build_when_it_cannot_ask()
    {
        var exitCode = await RunAsync("init");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("Which kind of build", console.Output);
        Assert.True(File.Exists(root / "build" / "_build.csproj"));
        Assert.False(File.Exists(root / "Build.cs"));
    }

    [Fact]
    public async Task Init_does_not_overwrite_existing_files()
    {
        Write("build/Build.cs", "// my own file\n");

        var exitCode = await RunAsync("init", "--project");

        Assert.Equal(1, exitCode);
        Assert.Contains("already exists", console.Output);
        Assert.Equal("// my own file\n", File.ReadAllText(root / "build" / "Build.cs"));
        Assert.False(File.Exists(root / "build" / "_build.csproj"));
    }

    [Fact]
    public async Task Init_creates_bootstrap_scripts_by_default()
    {
        var exitCode = await RunAsync("init", "--single-file");

        Assert.Equal(0, exitCode);

        var shellScript = File.ReadAllText(root / "build.sh");
        Assert.StartsWith("#!/usr/bin/env sh\n", shellScript);
        Assert.Contains("https://dot.net/v1/dotnet-install.sh", shellScript);
        Assert.Contains("--channel \"$DOTNET_CHANNEL\"", shellScript);
        Assert.Contains("https://dot.net/v1/dotnet-install.ps1", File.ReadAllText(root / "build.ps1"));
        Assert.Contains("%~dp0build.ps1", File.ReadAllText(root / "build.cmd"));
        Assert.Equal(".dotnet/\n", File.ReadAllText(root / ".gitignore"));

        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(root / "build.sh").HasFlag(UnixFileMode.UserExecute));
        }
    }

    [Fact]
    public async Task Init_skips_the_bootstrap_scripts_with_no_bootstrap()
    {
        var exitCode = await RunAsync("init", "--project", "--no-bootstrap");

        Assert.Equal(0, exitCode);
        Assert.Equal([".baun", "build"], Directory.GetFileSystemEntries(root).Select(Path.GetFileName).Order());
        Assert.Equal("{}\n", File.ReadAllText(root / ".baun" / "parameters.json"));
    }

    [Theory]
    [InlineData("--single-file")]
    [InlineData("--project")]
    public async Task Init_creates_an_empty_parameters_file(string kind)
    {
        var exitCode = await RunAsync("init", kind);

        Assert.Equal(0, exitCode);
        Assert.Equal("{}\n", File.ReadAllText(root / ".baun" / "parameters.json"));
        Assert.Contains("Created " + Path.Combine(".baun", "parameters.json"), console.Output);
    }

    [Fact]
    public async Task Init_does_not_overwrite_an_existing_parameters_file()
    {
        Write(".baun/parameters.json", "{ \"Configuration\": \"Release\" }\n");

        var exitCode = await RunAsync("init", "--single-file");

        Assert.Equal(0, exitCode);
        Assert.Equal("{ \"Configuration\": \"Release\" }\n", File.ReadAllText(root / ".baun" / "parameters.json"));
        Assert.DoesNotContain("parameters.json", console.Output);
    }

    [Fact]
    public async Task Init_creates_the_parameters_file_in_an_existing_baun_folder_of_a_parent()
    {
        Directory.CreateDirectory(root / ".baun");
        var subfolder = root / "tools";
        Directory.CreateDirectory(subfolder);

        var exitCode = await RunInAsync(subfolder, "init", "--single-file", "--no-bootstrap");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(root / ".baun" / "parameters.json"));
        Assert.False(Directory.Exists(subfolder / ".baun"));
        Assert.Contains("Created " + Path.Combine("..", ".baun", "parameters.json"), console.Output);
    }

    [Fact]
    public async Task Init_creates_the_baun_folder_in_the_current_folder_when_there_is_none()
    {
        Directory.CreateDirectory(root / ".git"); // a git repository doesn't decide where .baun goes
        var subfolder = root / "tools";
        Directory.CreateDirectory(subfolder);

        var exitCode = await RunInAsync(subfolder, "init", "--single-file", "--no-bootstrap");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(subfolder / ".baun" / "parameters.json"));
        Assert.False(Directory.Exists(root / ".baun"));
    }

    private Task<int> RunInAsync(AbsolutePath directory, params string[] arguments) =>
        CliApp.RunAsync(arguments, directory, console, startInfo =>
        {
            startedProcesses.Add(startInfo);
            return Task.FromResult(0);
        });

    [Fact]
    public async Task Init_in_an_existing_build_creates_a_missing_parameters_file()
    {
        Write("Build.cs", "#:package Baun@0.1.0\n");

        var exitCode = await RunAsync("init");

        Assert.Equal(0, exitCode);
        Assert.Equal("{}\n", File.ReadAllText(root / ".baun" / "parameters.json"));
    }

    [Theory]
    [InlineData("--single-file")]
    [InlineData("--project")]
    public async Task Init_ignores_the_temp_folder_in_the_baun_folder(string kind)
    {
        var exitCode = await RunAsync("init", kind);

        Assert.Equal(0, exitCode);
        Assert.Equal("temp/\n", File.ReadAllText(root / ".baun" / ".gitignore"));
        Assert.Contains("Created " + Path.Combine(".baun", ".gitignore"), console.Output);
    }

    [Fact]
    public async Task Init_in_an_existing_build_adds_the_temp_folder_to_the_gitignore_of_the_baun_folder_once()
    {
        Write("Build.cs", "#:package Baun@0.1.0\n");
        Write(".baun/.gitignore", "secrets.json\n");

        await RunAsync("init");
        await RunAsync("init");

        Assert.Equal("secrets.json\ntemp/\n", File.ReadAllText(root / ".baun" / ".gitignore"));
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

        Assert.Equal(after, File.ReadAllText(root / ".gitignore"));
    }

    [Theory]
    [InlineData("--single-file", new[] { "Build.cs", "build.sh" })]
    [InlineData("--project", new[] { "build.sh" })]
    public async Task Init_stages_executable_files_in_a_git_repository(string kind, string[] executables)
    {
        Directory.CreateDirectory(root / ".git");

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
        Directory.CreateDirectory(root / ".git");
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
        Assert.Equal(BootstrapScripts.Create(root)[root / "build.sh"], File.ReadAllText(root / "build.sh"));
        Assert.True(File.Exists(root / "Build.cs"));
    }

    [Fact]
    public async Task Init_overwrites_an_existing_editorconfig_of_a_project_build()
    {
        Write("build/.editorconfig", "# my own config\n");

        var exitCode = await RunAsync("init", "--project");

        Assert.Equal(0, exitCode);
        Assert.Contains("Updated build" + Path.DirectorySeparatorChar + ".editorconfig", console.Output);
        Assert.Contains("csharp_default_private_modifier = implicit", File.ReadAllText(root / "build" / ".editorconfig"));
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
        var path = root / relativePath;
        Directory.CreateDirectory(path.Parent!);
        File.WriteAllText(path, content);
    }
}
