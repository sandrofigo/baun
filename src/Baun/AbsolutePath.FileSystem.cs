using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Baun;

partial class AbsolutePath
{
    /// <summary>
    /// Gets whether a file or a directory exists at this path.
    /// </summary>
    public bool Exists => FileExists || DirectoryExists;

    /// <summary>
    /// Gets whether a file exists at this path.
    /// </summary>
    public bool FileExists => File.Exists(_path);

    /// <summary>
    /// Gets whether a directory exists at this path.
    /// </summary>
    public bool DirectoryExists => Directory.Exists(_path);

    /// <summary>
    /// Creates the directory at this path, with its parents, unless it already exists.
    /// </summary>
    /// <returns>This path.</returns>
    public AbsolutePath CreateDirectory()
    {
        Directory.CreateDirectory(_path);
        return this;
    }

    /// <summary>
    /// Creates the directory at this path, or deletes everything in it if it already exists, such as for an output
    /// directory at the start of a build.
    /// </summary>
    /// <returns>This path.</returns>
    public AbsolutePath CreateOrCleanDirectory()
    {
        if (!DirectoryExists)
        {
            return CreateDirectory();
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(_path))
        {
            new AbsolutePath(entry).Delete();
        }

        return this;
    }

    /// <summary>
    /// Deletes the file or the directory, with everything in it, at this path. Does nothing if neither exists.
    /// </summary>
    /// <remarks>Read-only files, such as those in a <c>.git</c> folder, are deleted too.</remarks>
    public void Delete()
    {
        if (FileExists)
        {
            File.SetAttributes(_path, FileAttributes.Normal);
            File.Delete(_path);
        }
        else if (DirectoryExists)
        {
            ClearReadOnly(_path);
            Directory.Delete(_path, recursive: true);
        }
    }

    /// <summary>
    /// Copies the file or the directory, with everything in it, at this path to <paramref name="target"/>, creating
    /// the parent directories of <paramref name="target"/>. A directory is merged into an existing one.
    /// </summary>
    /// <param name="target">The path of the copy, not the directory to copy into: use <c>directory / path.Name</c> for that.</param>
    /// <param name="overwrite">Whether to overwrite existing files. Otherwise an existing file throws.</param>
    /// <returns><paramref name="target"/>.</returns>
    /// <exception cref="FileNotFoundException">Neither a file nor a directory exists at this path.</exception>
    /// <exception cref="IOException">
    /// A file exists at the target and <paramref name="overwrite"/> is <see langword="false"/>, or the target is this
    /// path or inside it.
    /// </exception>
    public AbsolutePath CopyTo(AbsolutePath target, bool overwrite = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        ThrowIfMissing();
        ThrowIfInside(target, "copy");

        if (target == this)
        {
            throw new IOException($"Can't copy {_path} to itself.");
        }

        if (FileExists)
        {
            target.Parent?.CreateDirectory();
            File.Copy(_path, target._path, overwrite);
            return target;
        }

        target.CreateDirectory();

        foreach (string directory in Directory.EnumerateDirectories(_path, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target._path, Path.GetRelativePath(_path, directory)));
        }

        foreach (string file in Directory.EnumerateFiles(_path, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target._path, Path.GetRelativePath(_path, file)), overwrite);
        }

        return target;
    }

    /// <summary>
    /// Moves the file or the directory at this path to <paramref name="target"/>, creating the parent directories of
    /// <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The new path, not the directory to move into: use <c>directory / path.Name</c> for that.</param>
    /// <param name="overwrite">
    /// Whether to replace an existing file or directory at <paramref name="target"/>. Otherwise an existing one throws.
    /// </param>
    /// <returns><paramref name="target"/>.</returns>
    /// <exception cref="FileNotFoundException">Neither a file nor a directory exists at this path.</exception>
    /// <exception cref="IOException">
    /// The target exists and <paramref name="overwrite"/> is <see langword="false"/>, or the target is a directory that
    /// contains this path.
    /// </exception>
    public AbsolutePath MoveTo(AbsolutePath target, bool overwrite = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        ThrowIfMissing();
        ThrowIfInside(target, "move");

        if (string.Equals(_path, target._path, StringComparison.Ordinal))
        {
            return target;
        }

        // A path that equals this one but differs in case is a rename on Windows and macOS. Its target exists, because
        // it's this file or directory, so it must not be deleted.
        bool isCaseRename = target == this;

        if (target.Exists && !isCaseRename)
        {
            if (!overwrite)
            {
                throw new IOException($"Can't move {_path} to {target._path}, because it already exists.");
            }

            if (target.Contains(this))
            {
                throw new IOException($"Can't replace {target._path} with {_path}, because it contains it.");
            }

            target.Delete();
        }

        target.Parent?.CreateDirectory();

        if (FileExists)
        {
            File.Move(_path, target._path);
        }
        else
        {
            try
            {
                Directory.Move(_path, target._path);
            }
            catch (IOException exception) when (IsOtherVolume(target, exception))
            {
                // Directory.Move can't move a directory to another volume.
                CopyTo(target);
                Delete();
            }
        }

        return target;
    }

    /// <summary>
    /// Returns whether <paramref name="exception"/> from <see cref="Directory.Move"/> means that <paramref name="target"/>
    /// is on another volume. Unix paths all have the root <c>/</c>, so only the error tells there.
    /// </summary>
    private bool IsOtherVolume(AbsolutePath target, IOException exception)
    {
        const int crossDeviceUnix = 18; // EXDEV
        const int notSameDeviceWindows = unchecked((int)0x80070011); // ERROR_NOT_SAME_DEVICE, such as for a mounted folder

        return exception.HResult is crossDeviceUnix or notSameDeviceWindows ||
               !string.Equals(Path.GetPathRoot(_path), Path.GetPathRoot(target._path), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the files in this directory and its subdirectories that match <paramref name="patterns"/>, sorted by path.
    /// </summary>
    /// <remarks>
    /// Patterns are relative to this directory and use <c>/</c> as the separator. <c>*</c> matches any characters in a
    /// name, and <c>**</c> matches any number of directories, as in <c>**/*.csproj</c>. A pattern that starts with
    /// <c>!</c> excludes what it matches, as in <c>!**/bin/**</c>. A directory that doesn't exist has no files, and
    /// subdirectories that can't be read are skipped.
    /// </remarks>
    /// <param name="patterns">The patterns to match, such as <c>src/**/*.cs</c>.</param>
    public IReadOnlyList<AbsolutePath> GlobFiles(params string[] patterns) => Glob(patterns, directories: false);

    /// <summary>
    /// Returns the directories in this directory and its subdirectories that match <paramref name="patterns"/>, sorted
    /// by path, such as <c>**/bin</c>.
    /// </summary>
    /// <remarks>The patterns work as in <see cref="GlobFiles"/>.</remarks>
    /// <param name="patterns">The patterns to match, such as <c>**/bin</c>.</param>
    public IReadOnlyList<AbsolutePath> GlobDirectories(params string[] patterns) => Glob(patterns, directories: true);

    private List<AbsolutePath> Glob(string[] patterns, bool directories)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        var matcher = new Matcher(_comparison);

        foreach (string pattern in patterns)
        {
            ArgumentException.ThrowIfNullOrEmpty(pattern, nameof(patterns));

            if (pattern.StartsWith('!'))
            {
                matcher.AddExclude(pattern[1..]);
            }
            else
            {
                matcher.AddInclude(pattern);
            }
        }

        if (!DirectoryExists)
        {
            return [];
        }

        // Executing the matcher on the directory, rather than matching a list of every path, skips the directories
        // that no pattern can match, such as bin for !**/bin/**.
        return matcher.Execute(new GlobDirectory(new DirectoryInfo(_path), parent: null, directories)).Files
            .Select(match => this / match.Path)
            .OrderBy(path => path._path, StringComparer.FromComparison(_comparison))
            .ToList();
    }

    /// <summary>
    /// A directory for <see cref="Matcher"/> that skips subdirectories that can't be read. With
    /// <paramref name="directoriesAsFiles"/>, it lists its subdirectories as files instead of its files, because the
    /// matcher only matches files.
    /// </summary>
    private sealed class GlobDirectory(DirectoryInfo directory, DirectoryInfoBase? parent, bool directoriesAsFiles, bool isParentPath = false)
        : DirectoryInfoBase
    {
        // Unlike the default, includes hidden and system entries, such as .git on Windows.
        private static readonly EnumerationOptions _options = new() { IgnoreInaccessible = true, AttributesToSkip = 0 };

        // The matcher compares the name of a parent directory with a .. segment of a pattern.
        public override string Name => isParentPath ? ".." : directory.Name;

        public override string FullName => directory.FullName;

        public override DirectoryInfoBase? ParentDirectory =>
            parent ?? (directory.Parent is { } info ? new GlobDirectory(info, null, directoriesAsFiles) : null);

        public override IEnumerable<FileSystemInfoBase> EnumerateFileSystemInfos()
        {
            if (!directory.Exists)
            {
                yield break;
            }

            foreach (FileSystemInfo info in directory.EnumerateFileSystemInfos("*", _options))
            {
                if (info is DirectoryInfo subdirectory)
                {
                    yield return new GlobDirectory(subdirectory, this, directoriesAsFiles);

                    if (directoriesAsFiles)
                    {
                        yield return new DirectoryAsFile(subdirectory, this);
                    }
                }
                else if (!directoriesAsFiles)
                {
                    yield return new FileInfoWrapper((FileInfo)info);
                }
            }
        }

        public override DirectoryInfoBase GetDirectory(string path) =>
            new GlobDirectory(new DirectoryInfo(Path.Combine(directory.FullName, path)), this, directoriesAsFiles, path == "..");

        public override FileInfoBase? GetFile(string path) => directoriesAsFiles
            ? new DirectoryAsFile(new DirectoryInfo(Path.Combine(directory.FullName, path)), this)
            : new FileInfoWrapper(new FileInfo(Path.Combine(directory.FullName, path)));
    }

    private sealed class DirectoryAsFile(DirectoryInfo directory, DirectoryInfoBase parent) : FileInfoBase
    {
        public override string Name => directory.Name;

        public override string FullName => directory.FullName;

        public override DirectoryInfoBase ParentDirectory => parent;
    }

    private void ThrowIfMissing()
    {
        if (!Exists)
        {
            throw new FileNotFoundException($"{_path} doesn't exist.", _path);
        }
    }

    private void ThrowIfInside(AbsolutePath target, string action)
    {
        if (DirectoryExists && Contains(target))
        {
            throw new IOException($"Can't {action} {_path} into itself ({target._path}).");
        }
    }

    /// <summary>
    /// Returns whether <paramref name="path"/> is inside this directory, at any depth. A path doesn't contain itself.
    /// </summary>
    internal bool Contains(AbsolutePath path)
    {
        // A root such as C:\ or / already ends in a separator.
        string prefix = Path.EndsInDirectorySeparator(_path) ? _path : _path + Path.DirectorySeparatorChar;

        return path != this && path._path.StartsWith(prefix, _comparison);
    }

    private static void ClearReadOnly(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            FileAttributes attributes = File.GetAttributes(file);

            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }
    }
}
