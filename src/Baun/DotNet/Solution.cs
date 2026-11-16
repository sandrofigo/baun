using System.Diagnostics.CodeAnalysis;
using System.IO.Enumeration;
using Microsoft.VisualStudio.SolutionPersistence;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace Baun.DotNet;

/// <summary>
/// A .NET solution file in the <c>.sln</c> or <c>.slnx</c> format, with its projects.
/// </summary>
/// <remarks>
/// Read a solution with <see cref="Read"/>, as in <c>Solution.Read(RootDirectory / "App.slnx")</c>. A solution converts
/// to a <see cref="string"/> with its path implicitly, so it can be passed to a process, as in <c>dotnet test {solution}</c>.
/// </remarks>
public sealed class Solution : IEquatable<Solution>
{
    private readonly List<Project> _projects = [];

    private Solution(AbsolutePath path) => Path = path;

    /// <summary>
    /// Gets the absolute path of the solution file.
    /// </summary>
    public AbsolutePath Path { get; }

    /// <summary>
    /// Gets the directory that contains the solution file.
    /// </summary>
    public AbsolutePath Directory => Path.Parent!;

    /// <summary>
    /// Gets the name of the solution, which is the name of its file without the extension, such as <c>App</c> for
    /// <c>App.slnx</c>.
    /// </summary>
    public string Name => Project.GetName(Path);

    /// <summary>
    /// Reads the solution file at <paramref name="path"/>, in the <c>.sln</c> or <c>.slnx</c> format.
    /// </summary>
    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    /// <exception cref="SolutionFormatException">The file isn't a <c>.sln</c> or <c>.slnx</c> file, or isn't valid.</exception>
    public static Solution Read(AbsolutePath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!path.FileExists)
        {
            throw new FileNotFoundException($"The solution {path} doesn't exist.", path);
        }

        ISolutionSerializer serializer = SolutionSerializers.GetSerializerByMoniker(path)
            ?? throw new SolutionFormatException($"{path} isn't a solution file. Use a .sln or .slnx file.");

        SolutionModel model;

        try
        {
            model = serializer.OpenAsync(path, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not IOException and not UnauthorizedAccessException)
        {
            throw new SolutionFormatException($"{path} isn't a valid solution file. {exception.Message}", exception);
        }

        var solution = new Solution(path);

        // .sln files separate directories with \, which isn't a separator on Linux and macOS. The order of the model
        // differs between .sln and .slnx files, so the projects are sorted by path.
        solution._projects.AddRange(model.SolutionProjects
            .Select(project => new Project(solution.Directory / project.FilePath.Replace('\\', '/'), solution))
            .OrderBy(project => (string)project.Path, StringComparer.OrdinalIgnoreCase));

        return solution;
    }

    /// <summary>
    /// Returns the projects of the solution, sorted by path. Projects in solution folders are included.
    /// </summary>
    public IReadOnlyList<Project> GetProjects() => _projects.AsReadOnly();

    /// <summary>
    /// Gets the project with <paramref name="name"/>, such as <c>App</c> for <c>App.csproj</c>, ignoring case.
    /// </summary>
    /// <exception cref="InvalidOperationException">The solution has no project with <paramref name="name"/>.</exception>
    public Project GetProject(string name) =>
        TryGetProject(name, out Project? project)
            ? project
            : throw new InvalidOperationException(
                $"The solution {Name} has no project named {name}. Projects: {string.Join(", ", _projects.Select(item => item.Name))}.");

    /// <summary>
    /// Tries to get the project with <paramref name="name"/>, such as <c>App</c> for <c>App.csproj</c>, ignoring case.
    /// If several projects have the name, it gets the first one.
    /// </summary>
    /// <returns><see langword="true"/> if the solution has a project with <paramref name="name"/>.</returns>
    public bool TryGetProject(string name, [NotNullWhen(true)] out Project? project)
    {
        ArgumentNullException.ThrowIfNull(name);

        project = _projects.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return project is not null;
    }

    /// <summary>
    /// Returns the projects whose name matches any of <paramref name="patterns"/>, ignoring case, sorted by path, such
    /// as <c>*.Tests</c>.
    /// </summary>
    /// <remarks>
    /// <c>*</c> matches any characters and <c>?</c> matches one character. A pattern that starts with <c>!</c> excludes
    /// the projects it matches, as in <c>GlobProjects("*", "!*.Tests")</c>.
    /// </remarks>
    /// <param name="patterns">The patterns to match the project names with, such as <c>*.Tests</c>.</param>
    public IReadOnlyList<Project> GlobProjects(params string[] patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        var includes = new List<string>();
        var excludes = new List<string>();

        foreach (string pattern in patterns)
        {
            ArgumentException.ThrowIfNullOrEmpty(pattern, nameof(patterns));

            if (pattern.StartsWith('!'))
            {
                excludes.Add(pattern[1..]);
            }
            else
            {
                includes.Add(pattern);
            }
        }

        return _projects
            .Where(project => includes.Any(pattern => Matches(pattern, project.Name)) && !excludes.Any(pattern => Matches(pattern, project.Name)))
            .ToList();
    }

    /// <summary>
    /// Converts <paramref name="solution"/> to the <see cref="string"/> of its path.
    /// </summary>
    public static implicit operator string(Solution solution) => solution.Path;

    /// <summary>
    /// Returns whether <paramref name="left"/> and <paramref name="right"/> have the same path.
    /// </summary>
    public static bool operator ==(Solution? left, Solution? right) => Equals(left, right);

    /// <summary>
    /// Returns whether <paramref name="left"/> and <paramref name="right"/> have different paths.
    /// </summary>
    public static bool operator !=(Solution? left, Solution? right) => !Equals(left, right);

    /// <inheritdoc/>
    public bool Equals(Solution? other) => other is not null && Path == other.Path;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Solution other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Path.GetHashCode();

    /// <summary>
    /// Returns the path of the solution file.
    /// </summary>
    public override string ToString() => Path;

    private static bool Matches(string pattern, string name) =>
        FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true);
}
