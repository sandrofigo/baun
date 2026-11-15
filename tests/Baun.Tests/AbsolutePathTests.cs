namespace Baun.Tests;

public sealed class AbsolutePathTests
{
    private static readonly string Root = Path.GetPathRoot(Path.GetTempPath())!;
    private static readonly AbsolutePath Base = (AbsolutePath)Path.Combine(Root, "repo");

    [Theory]
    [InlineData("relative")]
    [InlineData("relative/path")]
    [InlineData("")]
    public void Rejects_a_relative_path(string path)
    {
        Assert.Throws<ArgumentException>(() => (AbsolutePath)path);
    }

    [Fact]
    public void Removes_a_trailing_separator()
    {
        var path = (AbsolutePath)(Path.Combine(Root, "repo") + Path.DirectorySeparatorChar);

        Assert.Equal(Path.Combine(Root, "repo"), path.ToString());
    }

    [Fact]
    public void Keeps_the_separator_of_a_root()
    {
        Assert.Equal(Root, ((AbsolutePath)Root).ToString());
    }

    [Fact]
    public void Combines_with_a_relative_path()
    {
        Assert.Equal(Path.Combine(Root, "repo", "src", "App.cs"), (Base / "src" / "App.cs").ToString());
    }

    [Fact]
    public void Combines_with_several_segments_and_parent_directories()
    {
        Assert.Equal(Path.Combine(Root, "repo", "src", "app"), (Base / "src/app").ToString());
        Assert.Equal(Path.Combine(Root, "other"), (Base / "../other").ToString());
        Assert.Equal(Base, Base / "src" / "..");
    }

    [Fact]
    public void Doesnt_combine_with_a_rooted_path()
    {
        Assert.Throws<ArgumentException>(() => Base / Path.Combine(Root, "other"));
    }

    [Fact]
    public void Has_a_name_and_extension()
    {
        var path = Base / "src" / "App.cs";

        Assert.Equal("App.cs", path.Name);
        Assert.Equal(".cs", path.Extension);
        Assert.Equal("src", path.Parent!.Name);
        Assert.Equal(string.Empty, path.Parent.Extension);
    }

    [Fact]
    public void Has_a_parent_up_to_the_root()
    {
        var root = (AbsolutePath)Root;

        Assert.Equal(Base, (Base / "src").Parent);
        Assert.Equal(root, Base.Parent);
        Assert.Null(root.Parent);
        Assert.Equal(string.Empty, root.Name);
    }

    [Fact]
    public void Equals_the_same_path()
    {
        AbsolutePath first = Base / "src";
        AbsolutePath second = (AbsolutePath)Path.Combine(Root, "repo", "src");

        Assert.True(first == second);
        Assert.False(first != second);
        Assert.True(first.Equals((object)second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(Base, Base / "src");
        Assert.False(Base == null);
    }

    [Fact]
    public void Compares_case_insensitively_on_windows_and_macos()
    {
        bool equal = Base / "Src" == Base / "src";

        Assert.Equal(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), equal);
    }

    [Fact]
    public void Converts_to_a_string()
    {
        string path = Base / "src";

        Assert.Equal(Path.Combine(Root, "repo", "src"), path);
    }
}
