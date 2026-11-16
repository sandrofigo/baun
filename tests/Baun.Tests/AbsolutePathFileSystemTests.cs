using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Baun.Tests;

public sealed class AbsolutePathFileSystemTests : IDisposable
{
    private readonly AbsolutePath root = (AbsolutePath)Directory.CreateTempSubdirectory("baun-path-tests-").FullName;

    public void Dispose() => root.Delete();

    [Fact]
    public void Tells_whether_a_file_or_directory_exists()
    {
        var file = Write("a.txt");

        Assert.True(file.Exists);
        Assert.True(file.FileExists);
        Assert.False(file.DirectoryExists);
        Assert.True(root.DirectoryExists);
        Assert.False(root.FileExists);
        Assert.False((root / "missing").Exists);
    }

    [Fact]
    public void Creates_a_directory_with_its_parents()
    {
        var directory = (root / "a" / "b").CreateDirectory();

        Assert.True(directory.DirectoryExists);
        Assert.Equal(directory, directory.CreateDirectory()); // an existing directory is fine
    }

    [Fact]
    public void Creates_or_cleans_a_directory()
    {
        var directory = root / "out";
        Write("out/a.txt");
        Write("out/sub/b.txt");

        Assert.Equal(directory, directory.CreateOrCleanDirectory());
        Assert.True(directory.DirectoryExists);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));

        var created = (root / "new").CreateOrCleanDirectory();
        Assert.True(created.DirectoryExists);
    }

    [Fact]
    public void Deletes_a_file()
    {
        var file = Write("a.txt");

        file.Delete();

        Assert.False(file.Exists);
    }

    [Fact]
    public void Deletes_a_directory_with_read_only_files()
    {
        var file = Write("dir/sub/a.txt");
        File.SetAttributes(file, FileAttributes.ReadOnly);

        (root / "dir").Delete();

        Assert.False((root / "dir").Exists);
    }

    [Fact]
    public void Deleting_a_missing_path_does_nothing()
    {
        (root / "missing").Delete();
    }

    [Fact]
    public void Copies_a_file_and_creates_the_target_directory()
    {
        var file = Write("a.txt", "content");

        var copy = file.CopyTo(root / "out" / "b.txt");

        Assert.Equal(root / "out" / "b.txt", copy);
        Assert.Equal("content", File.ReadAllText(copy));
        Assert.True(file.Exists);
    }

    [Fact]
    public void Copying_a_file_overwrites_only_when_asked()
    {
        var file = Write("a.txt", "new");
        var target = Write("b.txt", "old");

        Assert.Throws<IOException>(() => file.CopyTo(target));
        Assert.Equal("old", File.ReadAllText(target));

        file.CopyTo(target, overwrite: true);
        Assert.Equal("new", File.ReadAllText(target));
    }

    [Fact]
    public void Copies_a_directory_with_everything_in_it()
    {
        Write("src/a.txt", "a");
        Write("src/sub/b.txt", "b");
        (root / "src" / "empty").CreateDirectory();

        var copy = (root / "src").CopyTo(root / "copy");

        Assert.Equal("a", File.ReadAllText(copy / "a.txt"));
        Assert.Equal("b", File.ReadAllText(copy / "sub" / "b.txt"));
        Assert.True((copy / "empty").DirectoryExists);
        Assert.True((root / "src" / "a.txt").Exists);
    }

    [Fact]
    public void Copying_a_directory_merges_it_into_an_existing_one()
    {
        Write("src/a.txt", "new");
        Write("copy/a.txt", "old");
        Write("copy/keep.txt", "keep");

        Assert.Throws<IOException>(() => (root / "src").CopyTo(root / "copy"));

        (root / "src").CopyTo(root / "copy", overwrite: true);

        Assert.Equal("new", File.ReadAllText(root / "copy" / "a.txt"));
        Assert.Equal("keep", File.ReadAllText(root / "copy" / "keep.txt"));
    }

    [Fact]
    public void Doesnt_copy_a_directory_into_itself()
    {
        Write("src/a.txt");

        Assert.Throws<IOException>(() => (root / "src").CopyTo(root / "src" / "copy"));
    }

    [Fact]
    public void Tells_whether_a_directory_contains_a_path()
    {
        // Tests the check that keeps CopyTo and MoveTo from copying a directory into itself, without copying a whole drive.
        var drive = (AbsolutePath)Path.GetPathRoot(root)!;

        Assert.True(drive.Contains(root));
        Assert.True(root.Contains(root / "a" / "b"));
        Assert.False(root.Contains(root));
        Assert.False(drive.Contains(drive));
        Assert.False((root / "a").Contains(root / "ab"));
        Assert.False((root / "a").Contains(root));
    }

    [Fact]
    public void Doesnt_copy_a_path_to_itself()
    {
        var file = Write("dir/a.txt", "content");
        var directory = root / "dir";

        var fileError = Assert.Throws<IOException>(() => file.CopyTo(file, overwrite: true));
        var directoryError = Assert.Throws<IOException>(() => directory.CopyTo(directory, overwrite: true));

        Assert.Equal($"Can't copy {file} to itself.", fileError.Message);
        Assert.Equal($"Can't copy {directory} to itself.", directoryError.Message);
        Assert.Equal("content", File.ReadAllText(file));
    }

    [Fact]
    public void Copying_a_missing_path_throws()
    {
        Assert.Throws<FileNotFoundException>(() => (root / "missing").CopyTo(root / "copy"));
    }

    [Fact]
    public void Moves_a_file_and_creates_the_target_directory()
    {
        var file = Write("a.txt", "content");

        var moved = file.MoveTo(root / "out" / "b.txt");

        Assert.False(file.Exists);
        Assert.Equal("content", File.ReadAllText(moved));
    }

    [Fact]
    public void Moves_a_directory()
    {
        Write("src/sub/a.txt", "a");

        var moved = (root / "src").MoveTo(root / "out" / "moved");

        Assert.False((root / "src").Exists);
        Assert.Equal("a", File.ReadAllText(moved / "sub" / "a.txt"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Moving_renames_a_file_or_directory_that_differs_only_in_case(bool overwrite)
    {
        var file = Write("readme.md", "content");
        Write("docs/a.txt", "a");

        var movedFile = file.MoveTo(root / "README.md", overwrite);
        var movedDirectory = (root / "docs").MoveTo(root / "Docs", overwrite);

        Assert.Equal(["Docs", "README.md"], Directory.GetFileSystemEntries(root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal("content", File.ReadAllText(movedFile));
        Assert.Equal("a", File.ReadAllText(movedDirectory / "a.txt"));
    }

    [Fact]
    public void Moving_a_path_to_itself_does_nothing()
    {
        var file = Write("a.txt", "content");

        Assert.Equal(file, file.MoveTo(file, overwrite: true));
        Assert.Equal("content", File.ReadAllText(file));
    }

    [Fact]
    public void Moves_a_directory_to_another_file_system()
    {
        // On Linux, /dev/shm is a tmpfs mount, separate from the temp directory, yet both paths have the root "/".
        Assert.SkipUnless(OperatingSystem.IsLinux() && Directory.Exists("/dev/shm"), "Needs /dev/shm on Linux.");

        Write("src/sub/a.txt", "a");
        var target = (AbsolutePath)"/dev/shm" / $"baun-path-tests-{Guid.NewGuid():N}";

        try
        {
            (root / "src").MoveTo(target);

            Assert.False((root / "src").Exists);
            Assert.Equal("a", File.ReadAllText(target / "sub" / "a.txt"));
        }
        finally
        {
            target.Delete();
        }
    }

    [Fact]
    public void Moving_replaces_an_existing_target_only_when_asked()
    {
        Write("src/a.txt", "new");
        Write("target/old.txt", "old");

        Assert.Throws<IOException>(() => (root / "src").MoveTo(root / "target"));
        Assert.True((root / "src").Exists);

        (root / "src").MoveTo(root / "target", overwrite: true);

        Assert.Equal("new", File.ReadAllText(root / "target" / "a.txt"));
        Assert.False((root / "target" / "old.txt").Exists);
    }

    [Fact]
    public void Doesnt_replace_a_target_that_contains_the_moved_path()
    {
        var file = Write("out/tmp/pkg/a.txt", "a");

        Assert.Throws<IOException>(() => (root / "out" / "tmp" / "pkg").MoveTo(root / "out", overwrite: true));
        Assert.Throws<IOException>(() => file.MoveTo(root / "out", overwrite: true));

        Assert.Equal("a", File.ReadAllText(file));
    }

    [Fact]
    public void Moving_a_missing_path_throws()
    {
        Assert.Throws<FileNotFoundException>(() => (root / "missing").MoveTo(root / "target"));
    }

    [Fact]
    public void Globs_files()
    {
        Write("src/App/App.csproj");
        Write("src/App/Program.cs");
        Write("src/Lib/Lib.csproj");
        Write("src/Lib/x/Lib.csproj");
        Write("tests/Tests.csproj");

        Assert.Equal(
            [root / "src" / "App" / "App.csproj", root / "src" / "Lib" / "Lib.csproj", root / "src" / "Lib" / "x" / "Lib.csproj", root / "tests" / "Tests.csproj"],
            root.GlobFiles("**/*.csproj"));
        Assert.Equal([root / "src" / "App" / "Program.cs"], root.GlobFiles("src/*/*.cs"));
    }

    [Fact]
    public void Globs_files_with_several_patterns_and_exclusions()
    {
        Write("src/App/App.csproj");
        Write("src/App/bin/App.dll");
        Write("src/App/Program.cs");
        Write("tests/Tests.csproj");

        Assert.Equal(
            [root / "src" / "App" / "App.csproj", root / "src" / "App" / "Program.cs", root / "tests" / "Tests.csproj"],
            root.GlobFiles("**/*.csproj", "src/**/*", "!**/bin/**"));
    }

    [Fact]
    public void Globs_directories()
    {
        Write("src/App/bin/App.dll");
        Write("src/App/obj/App.dll");
        Write("src/Lib/bin/Lib.dll");

        Assert.Equal([root / "src" / "App" / "bin", root / "src" / "Lib" / "bin"], root.GlobDirectories("**/bin"));
        Assert.Equal(
            [root / "src" / "App" / "bin", root / "src" / "App" / "obj"],
            root.GlobDirectories("**/bin", "**/obj", "!src/Lib/**"));
    }

    [Fact]
    public void Globbing_skips_directories_that_cant_be_read()
    {
        Write("src/a.txt");
        Write("src/locked/b.txt");
        var locked = root / "src" / "locked";

        using (Deny(locked))
        {
            Assert.SkipWhen(CanRead(locked), "The current user can read any directory.");

            Assert.Equal([root / "src" / "a.txt"], root.GlobFiles("**/*.txt"));
            Assert.Equal([root / "src" / "a.txt"], root.GlobFiles("**/*.txt", "!**/locked/**"));
            Assert.Equal([root / "src", locked], root.GlobDirectories("**"));
        }
    }

    [Fact]
    public void Globs_with_literal_patterns()
    {
        Write("src/App/App.csproj");

        Assert.Equal([root / "src" / "App" / "App.csproj"], root.GlobFiles("src/App/App.csproj"));
        Assert.Equal([root / "src" / "App"], root.GlobDirectories("src/App"));
        Assert.Empty(root.GlobFiles("missing/App.csproj"));
        Assert.Equal([root / "src" / "App" / "App.csproj"], (root / "src" / "App").GlobFiles("../App/*.csproj"));
    }

    [Fact]
    public void Globbing_a_missing_directory_finds_nothing()
    {
        Assert.Empty((root / "missing").GlobFiles("**/*"));
        Assert.Empty((root / "missing").GlobDirectories("**"));
    }

    /// <summary>
    /// Denies the current user reading <paramref name="directory"/> until the result is disposed.
    /// </summary>
    private static IDisposable Deny(AbsolutePath directory) =>
        OperatingSystem.IsWindows() ? DenyOnWindows(directory) : DenyOnUnix(directory);

    [SupportedOSPlatform("windows")]
    private static Restore DenyOnWindows(AbsolutePath directory)
    {
        var info = new DirectoryInfo(directory);
        var security = info.GetAccessControl();
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        security.AddAccessRule(rule);
        info.SetAccessControl(security);

        return new Restore(() => AllowOnWindows(info, security, rule));
    }

    [SupportedOSPlatform("windows")]
    private static void AllowOnWindows(DirectoryInfo info, DirectorySecurity security, FileSystemAccessRule rule)
    {
        security.RemoveAccessRule(rule);
        info.SetAccessControl(security);
    }

    [UnsupportedOSPlatform("windows")]
    private static Restore DenyOnUnix(AbsolutePath directory)
    {
        UnixFileMode mode = File.GetUnixFileMode(directory);
        File.SetUnixFileMode(directory, UnixFileMode.None);

        return new Restore(() => AllowOnUnix(directory, mode));
    }

    [UnsupportedOSPlatform("windows")]
    private static void AllowOnUnix(AbsolutePath directory, UnixFileMode mode) => File.SetUnixFileMode(directory, mode);

    private static bool CanRead(AbsolutePath directory)
    {
        try
        {
            _ = Directory.EnumerateFileSystemEntries(directory).ToList();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    private AbsolutePath Write(string relativePath, string content = "")
    {
        var path = root / relativePath;
        path.Parent!.CreateDirectory();
        File.WriteAllText(path, content);
        return path;
    }
}
