using System.Diagnostics;

namespace Baun;

/// <summary>
/// An absolute path of a file or directory, such as <c>BaunBuild.RootDirectory</c>.
/// </summary>
/// <remarks>
/// Combine a path with a relative one using <c>/</c>, for example <c>RootDirectory / "artifacts" / "packages"</c>.
/// An absolute path converts to a <see cref="string"/> implicitly, so it can be passed to <see cref="File"/>,
/// <see cref="Directory"/> and <see cref="Path"/> methods. A string converts to an absolute path with an explicit cast:
/// <c>(AbsolutePath)"/tmp/output"</c>. Paths are compared case-insensitively on Windows and macOS.
/// </remarks>
[DebuggerDisplay($"{{{nameof(_path)}}}")]
public sealed partial class AbsolutePath : IEquatable<AbsolutePath>
{
    private static readonly StringComparison _comparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly string _path;

    private AbsolutePath(string path) =>
        _path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>
    /// Gets the name of the file or directory, with its extension, or an empty string for a root such as <c>C:\</c>.
    /// </summary>
    public string Name => Path.GetFileName(_path);

    /// <summary>
    /// Gets the extension of the file, including the period, such as <c>.cs</c>, or an empty string if it has none.
    /// </summary>
    public string Extension => Path.GetExtension(_path);

    /// <summary>
    /// Gets the directory that contains this path, or <see langword="null"/> for a root such as <c>C:\</c> or <c>/</c>.
    /// </summary>
    public AbsolutePath? Parent => Path.GetDirectoryName(_path) is { } parent ? new AbsolutePath(parent) : null;

    /// <summary>
    /// Converts <paramref name="path"/> to an absolute path.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="path"/> isn't an absolute path.</exception>
    public static explicit operator AbsolutePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"'{path}' isn't an absolute path.", nameof(path));
        }

        return new AbsolutePath(path);
    }

    /// <summary>
    /// Converts <paramref name="path"/> to a <see cref="string"/>.
    /// </summary>
    public static implicit operator string(AbsolutePath path) => path._path;

    /// <summary>
    /// Combines <paramref name="left"/> with the relative path <paramref name="right"/>, which may contain several
    /// segments and <c>..</c>, such as <c>"src/app"</c> or <c>"../other"</c>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="right"/> is rooted.</exception>
    public static AbsolutePath operator /(AbsolutePath left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (Path.IsPathRooted(right))
        {
            throw new ArgumentException($"'{right}' is rooted, so it can't be combined with {left._path}.", nameof(right));
        }

        return new AbsolutePath(Path.Combine(left._path, right));
    }

    /// <summary>
    /// Returns whether <paramref name="left"/> and <paramref name="right"/> are the same path.
    /// </summary>
    public static bool operator ==(AbsolutePath? left, AbsolutePath? right) => Equals(left, right);

    /// <summary>
    /// Returns whether <paramref name="left"/> and <paramref name="right"/> are different paths.
    /// </summary>
    public static bool operator !=(AbsolutePath? left, AbsolutePath? right) => !Equals(left, right);

    /// <inheritdoc/>
    public bool Equals(AbsolutePath? other) => other is not null && string.Equals(_path, other._path, _comparison);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is AbsolutePath other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _path.GetHashCode(_comparison);

    /// <summary>
    /// Returns the path as a string.
    /// </summary>
    public override string ToString() => _path;
}
