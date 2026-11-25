using System.IO.Compression;
using System.Text.Json;

namespace Baun;

partial class AbsolutePath
{
    // System.Text.Json defaults, so properties keep the names they have in C#.
    private static readonly JsonSerializerOptions _jsonWriteOptions = new() { WriteIndented = true };

    private static readonly JsonSerializerOptions _jsonWriteMinimizedOptions = new() { WriteIndented = false };

    private static readonly JsonSerializerOptions _jsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Reads the file at this path as one string.
    /// </summary>
    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    public string ReadAllText() => File.ReadAllText(_path);

    /// <summary>
    /// Reads the lines of the file at this path.
    /// </summary>
    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    public string[] ReadAllLines() => File.ReadAllLines(_path);

    /// <summary>
    /// Writes <paramref name="content"/> to the file at this path as UTF-8, replacing the file if it exists and
    /// creating its parent directories.
    /// </summary>
    /// <param name="content">The text to write.</param>
    /// <returns>This path.</returns>
    public AbsolutePath WriteAllText(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Parent?.CreateDirectory();
        File.WriteAllText(_path, content);
        return this;
    }

    /// <summary>
    /// Writes <paramref name="lines"/>, each followed by a line break, to the file at this path as UTF-8, replacing the
    /// file if it exists and creating its parent directories.
    /// </summary>
    /// <param name="lines">The lines to write.</param>
    /// <returns>This path.</returns>
    public AbsolutePath WriteAllLines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        Parent?.CreateDirectory();
        File.WriteAllLines(_path, lines);
        return this;
    }

    /// <summary>
    /// Appends <paramref name="content"/> to the file at this path as UTF-8, creating the file and its parent
    /// directories if they don't exist.
    /// </summary>
    /// <param name="content">The text to append.</param>
    /// <returns>This path.</returns>
    public AbsolutePath AppendAllText(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Parent?.CreateDirectory();
        File.AppendAllText(_path, content);
        return this;
    }

    /// <summary>
    /// Appends <paramref name="lines"/>, each followed by a line break, to the file at this path as UTF-8, creating the
    /// file and its parent directories if they don't exist.
    /// </summary>
    /// <param name="lines">The lines to append.</param>
    /// <returns>This path.</returns>
    public AbsolutePath AppendAllLines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        Parent?.CreateDirectory();
        File.AppendAllLines(_path, lines);
        return this;
    }

    /// <summary>
    /// Writes <paramref name="value"/> as JSON to the file at this path, replacing the file if it exists and creating
    /// its parent directories.
    /// </summary>
    /// <remarks>
    /// Properties keep the names they have in C#, unless <paramref name="options"/> sets a naming policy, such as
    /// <see cref="JsonNamingPolicy.CamelCase"/>.
    /// </remarks>
    /// <param name="value">The value to write.</param>
    /// <param name="indented">Whether to format the JSON with line breaks and indentation, or write it on one line.</param>
    /// <param name="options">
    /// The options to serialize with instead of the defaults. <paramref name="indented"/> overrides their
    /// <see cref="JsonSerializerOptions.WriteIndented"/>.
    /// </param>
    /// <returns>This path.</returns>
    public AbsolutePath WriteJson<T>(T value, bool indented = true, JsonSerializerOptions? options = null)
    {
        JsonSerializerOptions serializerOptions = options is null
            ? indented ? _jsonWriteOptions : _jsonWriteMinimizedOptions
            : new JsonSerializerOptions(options) { WriteIndented = indented };

        return WriteAllText(JsonSerializer.Serialize(value, serializerOptions));
    }

    /// <summary>
    /// Reads the JSON in the file at this path as a <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// Property names are matched ignoring case, and comments and trailing commas are allowed, unless
    /// <paramref name="options"/> are given.
    /// </remarks>
    /// <param name="options">The options to deserialize with instead of the defaults.</param>
    /// <returns>The value, or <see langword="null"/> if the file contains the JSON <c>null</c>.</returns>
    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    /// <exception cref="JsonException">The file isn't valid JSON for <typeparamref name="T"/>.</exception>
    public T? ReadJson<T>(JsonSerializerOptions? options = null)
    {
        using FileStream stream = File.OpenRead(_path);
        return JsonSerializer.Deserialize<T>(stream, options ?? _jsonReadOptions);
    }

    /// <summary>
    /// Zips the file or the directory at this path to the archive <paramref name="archive"/>, creating its parent
    /// directories. A directory's files and subdirectories are at the root of the archive, without the directory
    /// itself; a file is the archive's only entry.
    /// </summary>
    /// <param name="archive">The path of the archive, such as <c>artifacts / "app.zip"</c>.</param>
    /// <param name="compressionLevel">How much to compress the files.</param>
    /// <param name="overwrite">Whether to replace an existing archive. Otherwise an existing one throws.</param>
    /// <returns><paramref name="archive"/>.</returns>
    /// <exception cref="FileNotFoundException">Neither a file nor a directory exists at this path.</exception>
    /// <exception cref="IOException">
    /// The archive exists and <paramref name="overwrite"/> is <see langword="false"/>, or the archive is this path or
    /// inside it.
    /// </exception>
    public AbsolutePath ZipTo(AbsolutePath archive, CompressionLevel compressionLevel = CompressionLevel.Optimal,
        bool overwrite = false)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ThrowIfMissing();
        ThrowIfInside(archive, "zip");

        if (archive == this)
        {
            throw new IOException($"Can't zip {_path} to itself.");
        }

        if (archive.Exists)
        {
            if (!overwrite)
            {
                throw new IOException($"Can't zip {_path} to {archive._path}, because it already exists.");
            }

            archive.Delete();
        }

        archive.Parent?.CreateDirectory();

        if (DirectoryExists)
        {
            ZipFile.CreateFromDirectory(_path, archive._path, compressionLevel, includeBaseDirectory: false);
        }
        else
        {
            using ZipArchive zip = ZipFile.Open(archive._path, ZipArchiveMode.Create);
            zip.CreateEntryFromFile(_path, Name, compressionLevel);
        }

        return archive;
    }

    /// <summary>
    /// Extracts the zip archive at this path into <paramref name="directory"/>, creating it. The files are merged into
    /// an existing directory.
    /// </summary>
    /// <param name="directory">The directory to extract into.</param>
    /// <param name="overwrite">Whether to overwrite existing files. Otherwise an existing file throws.</param>
    /// <returns><paramref name="directory"/>.</returns>
    /// <exception cref="FileNotFoundException">The archive doesn't exist.</exception>
    /// <exception cref="IOException">
    /// A file in the archive exists in <paramref name="directory"/> and <paramref name="overwrite"/> is
    /// <see langword="false"/>.
    /// </exception>
    public AbsolutePath UnzipTo(AbsolutePath directory, bool overwrite = false)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (!FileExists)
        {
            throw new FileNotFoundException($"{_path} doesn't exist.", _path);
        }

        ZipFile.ExtractToDirectory(_path, directory._path, overwrite);
        return directory;
    }
}
