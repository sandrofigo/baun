namespace Baun.DotNet;

/// <summary>
/// A .NET project file, such as <c>src/App/App.csproj</c>.
/// </summary>
/// <remarks>
/// Read a project with <see cref="Read"/>, or get the projects of a solution with <see cref="DotNet.Solution.GetProjects"/>.
/// A project converts to a <see cref="string"/> with its path implicitly, so it can be passed to a process, as in
/// <c>dotnet build {project}</c>.
/// </remarks>
public sealed class Project : IEquatable<Project>
{
    internal Project(AbsolutePath path, Solution? solution)
    {
        Path = path;
        Solution = solution;
    }

    /// <summary>
    /// Gets the absolute path of the project file.
    /// </summary>
    public AbsolutePath Path { get; }

    /// <summary>
    /// Gets the directory that contains the project file.
    /// </summary>
    public AbsolutePath Directory => Path.Parent!;

    /// <summary>
    /// Gets the name of the project, which is the name of its file without the extension, such as <c>App</c> for
    /// <c>App.csproj</c>.
    /// </summary>
    public string Name => GetName(Path);

    /// <summary>
    /// Gets the solution the project was read from, or <see langword="null"/> if it was read by itself with
    /// <see cref="Read"/>.
    /// </summary>
    public Solution? Solution { get; }

    /// <summary>
    /// Reads the project file at <paramref name="path"/>, such as a <c>.csproj</c> file.
    /// </summary>
    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    public static Project Read(AbsolutePath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!path.FileExists)
        {
            throw new FileNotFoundException($"The project {path} doesn't exist.", path);
        }

        return new Project(path, solution: null);
    }

    /// <summary>
    /// Converts <paramref name="project"/> to the <see cref="string"/> of its path.
    /// </summary>
    public static implicit operator string(Project project) => project.Path;

    /// <summary>
    /// Returns whether <paramref name="left"/> and <paramref name="right"/> have the same path.
    /// </summary>
    public static bool operator ==(Project? left, Project? right) => Equals(left, right);

    /// <summary>
    /// Returns whether <paramref name="left"/> and <paramref name="right"/> have different paths.
    /// </summary>
    public static bool operator !=(Project? left, Project? right) => !Equals(left, right);

    /// <inheritdoc/>
    public bool Equals(Project? other) => other is not null && Path == other.Path;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Project other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Path.GetHashCode();

    /// <summary>
    /// Returns the path of the project file.
    /// </summary>
    public override string ToString() => Path;

    internal static string GetName(AbsolutePath path) => System.IO.Path.GetFileNameWithoutExtension(path.Name);
}
