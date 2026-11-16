using Baun.DotNet;

namespace Baun.Tests;

public sealed class SolutionTests : IDisposable
{
    private const string Sln = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        # Visual Studio Version 17
        VisualStudioVersion = 17.0.31903.59
        MinimumVisualStudioVersion = 10.0.40219.1
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "src\App\App.csproj", "{11111111-1111-1111-1111-111111111111}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App.Core", "src\App.Core\App.Core.csproj", "{44444444-4444-4444-4444-444444444444}"
        EndProject
        Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "tests", "tests", "{22222222-2222-2222-2222-222222222222}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App.Tests", "tests\App.Tests\App.Tests.csproj", "{33333333-3333-3333-3333-333333333333}"
        EndProject
        Global
        	GlobalSection(NestedProjects) = preSolution
        		{33333333-3333-3333-3333-333333333333} = {22222222-2222-2222-2222-222222222222}
        	EndGlobalSection
        EndGlobal
        """;

    private const string Slnx = """
        <Solution>
          <Folder Name="/tests/">
            <Project Path="tests/App.Tests/App.Tests.csproj" />
          </Folder>
          <Project Path="src/App.Core/App.Core.csproj" />
          <Project Path="src/App/App.csproj" />
        </Solution>
        """;

    private readonly AbsolutePath root = (AbsolutePath)Directory.CreateTempSubdirectory("baun-solution-tests-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Theory]
    [InlineData("App.sln", Sln)]
    [InlineData("App.slnx", Slnx)]
    public void Reads_the_projects_of_a_solution(string fileName, string content)
    {
        var solution = Solution.Read(Write(fileName, content));

        Assert.Equal(root / fileName, solution.Path);
        Assert.Equal(root, solution.Directory);
        Assert.Equal("App", solution.Name);
        Assert.Equal(
            [root / "src" / "App.Core" / "App.Core.csproj", root / "src" / "App" / "App.csproj", root / "tests" / "App.Tests" / "App.Tests.csproj"],
            solution.GetProjects().Select(project => project.Path));
        Assert.All(solution.GetProjects(), project => Assert.Same(solution, project.Solution));
    }

    [Theory]
    [InlineData("App.sln", Sln)]
    [InlineData("App.slnx", Slnx)]
    public void Gets_a_project_by_name_ignoring_case(string fileName, string content)
    {
        var solution = Solution.Read(Write(fileName, content));

        Assert.Equal(root / "tests" / "App.Tests" / "App.Tests.csproj", solution.GetProject("app.tests").Path);
        Assert.True(solution.TryGetProject("APP", out Project? project));
        Assert.Equal("App", project.Name);
        Assert.False(solution.TryGetProject("Missing", out project));
        Assert.Null(project);
    }

    [Fact]
    public void Fails_to_get_a_project_that_is_not_in_the_solution()
    {
        var solution = Solution.Read(Write("App.slnx", Slnx));

        var exception = Assert.Throws<InvalidOperationException>(() => solution.GetProject("Missing"));

        Assert.StartsWith("The solution App has no project named Missing. Projects: ", exception.Message);
        Assert.Contains("App.Tests", exception.Message);
    }

    [Theory]
    [InlineData(new[] { "*" }, new[] { "App.Core", "App", "App.Tests" })]
    [InlineData(new[] { "*.tests" }, new[] { "App.Tests" })]
    [InlineData(new[] { "App.????" }, new[] { "App.Core" })]
    [InlineData(new[] { "*", "!*.Tests" }, new[] { "App.Core", "App" })]
    [InlineData(new[] { "App", "*.Core" }, new[] { "App.Core", "App" })]
    [InlineData(new[] { "!App" }, new string[0])]
    [InlineData(new[] { "Other*" }, new string[0])]
    public void Globs_projects_by_name(string[] patterns, string[] expected)
    {
        var solution = Solution.Read(Write("App.slnx", Slnx));

        Assert.Equal(expected, solution.GlobProjects(patterns).Select(project => project.Name));
    }

    [Fact]
    public void Globs_projects_in_the_order_of_GetProjects()
    {
        var solution = Solution.Read(Write("App.slnx", Slnx));
        var projects = solution.GetProjects();

        Assert.Equal(projects.Where(project => project.Name != "App.Core"), solution.GlobProjects("*", "!App.Core"));
    }

    [Fact]
    public void Reads_a_solution_without_projects()
    {
        var solution = Solution.Read(Write("Empty.slnx", "<Solution />"));

        Assert.Empty(solution.GetProjects());
        Assert.Empty(solution.GlobProjects("*"));
    }

    [Fact]
    public void Reads_the_solution_of_this_repository()
    {
        var solution = Solution.Read(BaunBuild.RootDirectory / "Baun.slnx");

        Assert.Contains("Baun.Tests", solution.GetProjects().Select(project => project.Name));
        Assert.True(solution.GetProject("Baun").Path.FileExists);
    }

    [Fact]
    public void Fails_for_a_missing_file()
    {
        var exception = Assert.Throws<FileNotFoundException>(() => Solution.Read(root / "Missing.slnx"));

        Assert.Equal(root / "Missing.slnx", exception.FileName);
    }

    [Fact]
    public void Fails_for_a_file_that_is_not_a_solution()
    {
        var exception = Assert.Throws<SolutionFormatException>(() => Solution.Read(Write("App.txt", Slnx)));

        Assert.Contains("isn't a solution file", exception.Message);
    }

    [Theory]
    [InlineData("App.slnx", "<Solution><Project")]
    [InlineData("App.sln", "not a solution")]
    public void Fails_for_an_invalid_solution(string fileName, string content)
    {
        var exception = Assert.Throws<SolutionFormatException>(() => Solution.Read(Write(fileName, content)));

        Assert.Contains("isn't a valid solution file", exception.Message);
    }

    [Fact]
    public void Converts_to_its_path_and_compares_by_path()
    {
        var path = Write("App.slnx", Slnx);
        var solution = Solution.Read(path);

        string text = solution;

        Assert.Equal(path, text);
        Assert.Equal(path, solution.ToString());
        Assert.Equal(solution, Solution.Read(path));
        Assert.True(solution == Solution.Read(path));
        Assert.Equal(solution.GetHashCode(), Solution.Read(path).GetHashCode());
        Assert.NotEqual(solution, Solution.Read(Write("Other.slnx", Slnx)));
    }

    private AbsolutePath Write(string fileName, string content)
    {
        var path = root / fileName;
        File.WriteAllText(path, content);
        return path;
    }
}
