using Baun.Tools.DotNet;

namespace Baun.Tests;

public sealed class ProjectTests : IDisposable
{
    private readonly AbsolutePath root = (AbsolutePath)Directory.CreateTempSubdirectory("baun-project-tests-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Theory]
    [InlineData("App.csproj", "App")]
    [InlineData("App.Tests.csproj", "App.Tests")]
    [InlineData("Lib.fsproj", "Lib")]
    [InlineData("Legacy.vbproj", "Legacy")]
    public void Reads_a_project(string fileName, string name)
    {
        var path = WriteProject("src/" + fileName);

        var project = Project.Read(path);

        Assert.Equal(path, project.Path);
        Assert.Equal(root / "src", project.Directory);
        Assert.Equal(name, project.Name);
        Assert.Null(project.Solution);
    }

    [Fact]
    public void Fails_for_a_missing_file()
    {
        var exception = Assert.Throws<FileNotFoundException>(() => Project.Read(root / "Missing.csproj"));

        Assert.Equal(root / "Missing.csproj", exception.FileName);
        Assert.Contains("doesn't exist", exception.Message);
    }

    [Fact]
    public void Converts_to_its_path()
    {
        var path = WriteProject("App/App.csproj");
        var project = Project.Read(path);

        string text = project;

        Assert.Equal(path, text);
        Assert.Equal(path, project.ToString());
    }

    [Fact]
    public void Compares_projects_by_path()
    {
        var path = WriteProject("App/App.csproj");
        var other = WriteProject("Other/App.csproj");

        Assert.Equal(Project.Read(path), Project.Read(path));
        Assert.True(Project.Read(path) == Project.Read(path));
        Assert.Equal(Project.Read(path).GetHashCode(), Project.Read(path).GetHashCode());
        Assert.NotEqual(Project.Read(path), Project.Read(other));
        Assert.True(Project.Read(path) != Project.Read(other));
        Assert.False(Project.Read(path).Equals(null));
    }

    [Fact]
    public void A_project_of_a_solution_equals_the_same_project_read_by_itself()
    {
        var path = WriteProject("src/App/App.csproj");
        var solution = Solution.Read(Write("App.slnx", """<Solution><Project Path="src/App/App.csproj" /></Solution>"""));

        Assert.Equal(Project.Read(path), solution.GetProject("App"));
    }

    [Fact]
    public void Resolves_the_paths_of_a_solution_relative_to_its_directory()
    {
        var solution = Solution.Read(Write("build/App.sln", """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "..\src\App\App.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Build", "_build.csproj", "{22222222-2222-2222-2222-222222222222}"
            EndProject
            """));

        var app = solution.GetProject("App");
        var build = solution.GetProject("_build");

        Assert.Equal(root / "src" / "App" / "App.csproj", app.Path);
        Assert.Equal(root / "src" / "App", app.Directory);
        Assert.Equal(root / "build" / "_build.csproj", build.Path);
        Assert.Same(solution, app.Solution);
    }

    [Fact]
    public void Names_a_project_of_a_solution_after_its_file()
    {
        var solution = Solution.Read(Write("App.slnx", """<Solution><Project Path="src/App/App.Web.csproj" DisplayName="Web" /></Solution>"""));

        Assert.Equal("App.Web", Assert.Single(solution.GetProjects()).Name);
    }

    private AbsolutePath WriteProject(string relativePath) => Write(relativePath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

    private AbsolutePath Write(string relativePath, string content)
    {
        var path = root / relativePath;
        path.Parent!.CreateDirectory();
        File.WriteAllText(path, content);
        return path;
    }
}
