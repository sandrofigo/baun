using Baun.Cli;

namespace Baun.Tests;

public sealed class BuildLocatorTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("baun-tests-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void Finds_a_single_file_build_in_the_current_folder()
    {
        var buildFile = Write("Build.cs", "#!/usr/bin/env dotnet\n#:package Baun@0.1.0\n");

        Assert.Equal(new BuildLocation(buildFile, null), BuildLocator.Find(root));
    }

    [Fact]
    public void Treats_a_build_file_with_directives_as_a_single_file_build_even_next_to_a_project()
    {
        var buildFile = Write("Build.cs", "// My build\n#:package Baun@0.1.0\n");
        Write("App.csproj", "<Project />");

        Assert.Equal(new BuildLocation(buildFile, null), BuildLocator.Find(root));
    }

    [Fact]
    public void Finds_a_project_build_in_the_build_subfolder()
    {
        var buildFile = Write("build/Build.cs", "await BaunBuild.RunAsync<Build>();\n");
        var projectFile = Write("build/Build.csproj", "<Project />");

        Assert.Equal(new BuildLocation(buildFile, projectFile), BuildLocator.Find(root));
    }

    [Fact]
    public void Prefers_the_current_folder_over_the_build_subfolder()
    {
        var buildFile = Write("Build.cs", "#:package Baun@0.1.0\n");
        Write("build/Build.cs", "#:package Baun@0.1.0\n");

        Assert.Equal(new BuildLocation(buildFile, null), BuildLocator.Find(root));
    }

    [Fact]
    public void Returns_null_when_there_is_no_build()
    {
        Write("src/Program.cs", "");

        Assert.Null(BuildLocator.Find(root));
    }

    [Fact]
    public void Returns_null_for_a_project_build_file_without_a_project()
    {
        Write("Build.cs", "await BaunBuild.RunAsync<Build>();\n");

        Assert.Null(BuildLocator.Find(root));
    }

    private string Write(string relativePath, string content)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
