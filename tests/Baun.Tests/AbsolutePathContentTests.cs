using System.IO.Compression;
using System.Text.Json;

namespace Baun.Tests;

public sealed class AbsolutePathContentTests : IDisposable
{
    private readonly AbsolutePath root = (AbsolutePath)Directory.CreateTempSubdirectory("baun-content-tests-").FullName;

    public void Dispose() => root.Delete();

    [Fact]
    public void Writes_and_reads_text()
    {
        var file = root / "sub" / "a.txt";

        Assert.Equal(file, file.WriteAllText("hello\nworld"));
        Assert.Equal("hello\nworld", file.ReadAllText());

        file.WriteAllText("replaced");
        Assert.Equal("replaced", file.ReadAllText());
    }

    [Fact]
    public void Writes_and_reads_lines()
    {
        var file = root / "sub" / "lines.txt";

        Assert.Equal(file, file.WriteAllLines(["a", "b"]));
        Assert.Equal(["a", "b"], file.ReadAllLines());
    }

    [Fact]
    public void Appends_text_and_lines()
    {
        var file = root / "new" / "log.txt";

        Assert.Equal(file, file.AppendAllText("a"));
        file.AppendAllText("b");
        Assert.Equal("ab", file.ReadAllText());

        var lines = root / "new" / "lines.txt";
        Assert.Equal(lines, lines.AppendAllLines(["1"]));
        lines.AppendAllLines(["2", "3"]);
        Assert.Equal(["1", "2", "3"], lines.ReadAllLines());
    }

    [Fact]
    public void Reading_a_missing_file_throws()
    {
        Assert.Throws<FileNotFoundException>(() => (root / "missing.txt").ReadAllText());
        Assert.Throws<FileNotFoundException>(() => (root / "missing.txt").ReadAllLines());
        Assert.Throws<FileNotFoundException>(() => (root / "missing.json").ReadJson<Settings>());
    }

    [Fact]
    public void Writes_indented_json_with_csharp_names_by_default()
    {
        var file = root / "sub" / "settings.json";

        Assert.Equal(file, file.WriteJson(new Settings { Name = "app", Count = 2 }));

        string json = file.ReadAllText();
        Assert.Contains("\"Name\": \"app\"", json);
        Assert.Contains('\n', json);
    }

    [Fact]
    public void Writes_minimized_json()
    {
        var file = root / "settings.json";

        file.WriteJson(new Settings { Name = "app", Count = 2 }, indented: false);

        Assert.Equal("""{"Name":"app","Count":2}""", file.ReadAllText());
    }

    [Fact]
    public void Writes_json_with_custom_options()
    {
        var file = root / "settings.json";
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

        file.WriteJson(new Settings { Name = "app", Count = 2 }, indented: false, options);

        Assert.Equal("""{"name":"app","count":2}""", file.ReadAllText());
        Assert.True(options.WriteIndented); // the given options aren't changed
    }

    [Fact]
    public void Reads_json_ignoring_case_with_comments_and_trailing_commas()
    {
        var file = (root / "settings.json").WriteAllText("""
            {
                // a comment
                "name": "app",
                "COUNT": 2,
            }
            """);

        Settings? settings = file.ReadJson<Settings>();

        Assert.NotNull(settings);
        Assert.Equal("app", settings.Name);
        Assert.Equal(2, settings.Count);
    }

    [Fact]
    public void Reads_json_with_custom_options()
    {
        var file = (root / "settings.json").WriteAllText("""{"name":"app","Count":2}""");

        Settings? settings = file.ReadJson<Settings>(new JsonSerializerOptions());

        Assert.NotNull(settings);
        Assert.Null(settings.Name); // case-sensitive
        Assert.Equal(2, settings.Count);
    }

    [Fact]
    public void Round_trips_json()
    {
        var file = root / "settings.json";

        file.WriteJson(new Settings { Name = "app", Count = 3 });

        Assert.Equal(new Settings { Name = "app", Count = 3 }, file.ReadJson<Settings>());
    }

    [Fact]
    public void Zips_a_directory_with_relative_entries()
    {
        var source = root / "source";
        (source / "a.txt").WriteAllText("a");
        (source / "sub" / "b.txt").WriteAllText("b");
        var archive = root / "out" / "source.zip";

        Assert.Equal(archive, source.ZipTo(archive));

        using ZipArchive zip = ZipFile.OpenRead(archive);
        Assert.Equal(["a.txt", "sub/b.txt"], zip.Entries.Select(entry => entry.FullName.Replace('\\', '/')).Order());
    }

    [Fact]
    public void Zips_a_file_as_the_only_entry()
    {
        var file = (root / "a.txt").WriteAllText("a");
        var archive = root / "a.zip";

        file.ZipTo(archive, CompressionLevel.Fastest);

        using ZipArchive zip = ZipFile.OpenRead(archive);
        Assert.Equal("a.txt", Assert.Single(zip.Entries).FullName);
    }

    [Fact]
    public void Zipping_a_missing_path_throws()
    {
        Assert.Throws<FileNotFoundException>(() => (root / "missing").ZipTo(root / "missing.zip"));
    }

    [Fact]
    public void Zipping_to_an_existing_archive_throws_unless_overwriting()
    {
        var file = (root / "a.txt").WriteAllText("a");
        var archive = (root / "a.zip").WriteAllText("old");

        Assert.Throws<IOException>(() => file.ZipTo(archive));
        Assert.Equal("old", archive.ReadAllText());

        file.ZipTo(archive, overwrite: true);

        using ZipArchive zip = ZipFile.OpenRead(archive);
        Assert.Equal("a.txt", Assert.Single(zip.Entries).FullName);
    }

    [Fact]
    public void Zipping_into_the_directory_itself_throws()
    {
        var source = root / "source";
        (source / "a.txt").WriteAllText("a");

        Assert.Throws<IOException>(() => source.ZipTo(source / "source.zip"));
        Assert.False((source / "source.zip").Exists);
    }

    [Fact]
    public void Unzips_an_archive()
    {
        var source = root / "source";
        (source / "a.txt").WriteAllText("a");
        (source / "sub" / "b.txt").WriteAllText("b");
        var archive = source.ZipTo(root / "source.zip");
        var target = root / "target";

        Assert.Equal(target, archive.UnzipTo(target));

        Assert.Equal("a", (target / "a.txt").ReadAllText());
        Assert.Equal("b", (target / "sub" / "b.txt").ReadAllText());
    }

    [Fact]
    public void Unzipping_merges_and_throws_for_existing_files_unless_overwriting()
    {
        var archive = (root / "a.txt").WriteAllText("new").ZipTo(root / "a.zip");
        var target = root / "target";
        (target / "a.txt").WriteAllText("old");
        (target / "other.txt").WriteAllText("other");

        Assert.Throws<IOException>(() => archive.UnzipTo(target));
        Assert.Equal("old", (target / "a.txt").ReadAllText());

        archive.UnzipTo(target, overwrite: true);

        Assert.Equal("new", (target / "a.txt").ReadAllText());
        Assert.Equal("other", (target / "other.txt").ReadAllText());
    }

    [Fact]
    public void Unzipping_a_missing_archive_throws()
    {
        Assert.Throws<FileNotFoundException>(() => (root / "missing.zip").UnzipTo(root / "target"));
    }

    private sealed record Settings
    {
        public string? Name { get; init; }

        public int Count { get; init; }
    }
}
