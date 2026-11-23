using System.Text.Json;
using System.Text.Json.Serialization;

namespace Baun.Plan;

/// <summary>
/// A step of the plan page.
/// </summary>
/// <param name="Name">The name of the step.</param>
/// <param name="Dependencies">The names of the steps it depends on.</param>
/// <param name="Order">The position of the step in the execution plan, from 1, or <see langword="null"/> when it doesn't run.</param>
internal sealed record PlanStep(string Name, IReadOnlyList<string> Dependencies, int? Order)
{
    /// <summary>
    /// Gets the names of the steps it triggers.
    /// </summary>
    public IReadOnlyList<string> Triggers { get; init; } = [];

    /// <summary>
    /// Gets whether <c>--skip</c> skips the step.
    /// </summary>
    public bool Skipped { get; init; }

    /// <summary>
    /// Gets the condition the step runs under, or <see langword="null"/> when it has none.
    /// </summary>
    public string? Condition { get; init; }
}

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
            steps = steps.Select(step => new PageStep(
                step.Name,
                step.Dependencies,
                step.Order,
                step.Triggers.Count > 0 ? step.Triggers : null,
                step.Skipped ? true : null,
                step.Condition)),
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

    /// <summary>
    /// The JSON of a step. The values a step usually doesn't have are left out when they're null.
    /// </summary>
    private sealed record PageStep(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("dependencies")] IReadOnlyList<string> Dependencies,
        [property: JsonPropertyName("order")] int? Order,
        [property: JsonPropertyName("triggers"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Triggers,
        [property: JsonPropertyName("skipped"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Skipped,
        [property: JsonPropertyName("condition"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Condition);
}
