namespace Baun.Tests;

public sealed class TempDirectoryTests : IDisposable
{
    private readonly AbsolutePath root = (AbsolutePath)Directory.CreateTempSubdirectory("baun-temp-tests-").FullName;

    public void Dispose() => root.Delete();

    [Fact]
    public void The_temp_directory_is_in_the_baun_folder_of_the_repository()
    {
        Directory.CreateDirectory(root / ".baun");
        AbsolutePath subfolder = (root / "src" / "App").CreateDirectory();

        Assert.Equal(root / ".baun" / "temp", BaunBuild.FindTempDirectory(subfolder));
    }

    [Fact]
    public void The_temp_directory_is_in_the_temp_directory_of_the_system_without_a_baun_folder()
    {
        Assert.Equal((AbsolutePath)Path.GetTempPath() / "baun", BaunBuild.FindTempDirectory(root));
    }

    [Fact]
    public void Finding_the_temp_directory_does_not_create_it()
    {
        Directory.CreateDirectory(root / ".baun");

        Assert.False(BaunBuild.FindTempDirectory(root).Exists);
    }

    [Fact]
    public void The_temp_directory_exists()
    {
        Assert.True(BaunBuild.TempDirectory.DirectoryExists);
    }

    [Fact]
    public void Creates_empty_temp_files_with_unique_names()
    {
        AbsolutePath directory = root / "temp";

        AbsolutePath first = BaunBuild.CreateTempFile(directory, null);
        AbsolutePath second = BaunBuild.CreateTempFile(directory, null);

        Assert.NotEqual(first, second);
        Assert.Equal(directory, first.Parent);
        Assert.Equal(".tmp", first.Extension);
        Assert.True(first.FileExists);
        Assert.Equal(0, new FileInfo(first).Length);
    }

    [Theory]
    [InlineData(".json")]
    [InlineData("json")]
    public void Creates_temp_files_with_an_extension(string extension)
    {
        AbsolutePath file = BaunBuild.CreateTempFile(root, extension);

        Assert.Equal(".json", file.Extension);
        Assert.True(file.FileExists);
    }

    [Fact]
    public void Creates_empty_temp_directories_with_unique_names()
    {
        AbsolutePath directory = root / "temp";

        AbsolutePath first = BaunBuild.CreateTempDirectory(directory, null);
        AbsolutePath second = BaunBuild.CreateTempDirectory(directory, null);

        Assert.NotEqual(first, second);
        Assert.Equal(directory, first.Parent);
        Assert.True(first.DirectoryExists);
        Assert.Empty(Directory.EnumerateFileSystemEntries(first));
    }

    [Fact]
    public void Creates_temp_directories_with_a_prefix()
    {
        AbsolutePath directory = BaunBuild.CreateTempDirectory(root, "download-");

        Assert.StartsWith("download-", directory.Name, StringComparison.Ordinal);
        Assert.True(directory.Name.Length > "download-".Length);
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a\0b")]
    public void Rejects_names_with_separators_or_invalid_characters(string name)
    {
        Assert.Throws<ArgumentException>("prefix", () => BaunBuild.CreateTempDirectory(root, name));
        Assert.Throws<ArgumentException>("extension", () => BaunBuild.CreateTempFile(root, name));
    }
}
