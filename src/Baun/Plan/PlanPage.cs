using System.Text.Json;

namespace Baun.Plan;

/// <summary>
/// A step of the plan page.
/// </summary>
/// <param name="Name">The name of the step.</param>
/// <param name="Dependencies">The names of the steps it depends on.</param>
/// <param name="Order">The position of the step in the execution plan, from 1, or <see langword="null"/> when it doesn't run.</param>
internal sealed record PlanStep(string Name, IReadOnlyList<string> Dependencies, int? Order);

/// <summary>
/// The HTML page that shows the execution plan as a graph on an infinite canvas.
/// </summary>
internal static class PlanPage
{
    /// <summary>
    /// The file name of the page.
    /// </summary>
    public const string FileName = "plan.html";

    private const string Placeholder = "/*PLAN*/null";

    /// <summary>
    /// Returns the folder the page is written to: <c>.baun/temp</c> in the repository root of <paramref name="directory"/>,
    /// or the temp folder of the system when there is no <c>.baun</c> folder.
    /// </summary>
    public static AbsolutePath GetFolder(AbsolutePath directory) =>
        RepositoryRoot.TryFind(directory) is { } root
            ? root / RepositoryRoot.FolderName / RepositoryRoot.TempFolderName
            : (AbsolutePath)Path.GetTempPath() / "baun";

    /// <summary>
    /// Writes the page for <paramref name="steps"/> to <c>plan.html</c> in <paramref name="folder"/>, overwriting it.
    /// </summary>
    /// <returns>The path of the page.</returns>
    public static AbsolutePath Write(IReadOnlyList<PlanStep> steps, AbsolutePath folder)
    {
        AbsolutePath path = folder / FileName;
        Directory.CreateDirectory(folder);
        File.WriteAllText(path, Render(steps));
        return path;
    }

    /// <summary>
    /// Returns the HTML of the page.
    /// </summary>
    public static string Render(IReadOnlyList<PlanStep> steps)
    {
        // The default encoder escapes < and >, so a step name can't end the script element.
        string json = JsonSerializer.Serialize(new
        {
            steps = steps.Select(step => new { name = step.Name, dependencies = step.Dependencies, order = step.Order }),
        });

        return Template.Value.Replace(Placeholder, json, StringComparison.Ordinal);
    }

    private static readonly Lazy<string> Template = new(() =>
    {
        using Stream stream = typeof(PlanPage).Assembly.GetManifestResourceStream("Baun.Plan.PlanPage.html")
            ?? throw new InvalidOperationException("The plan page is missing from the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });
}
