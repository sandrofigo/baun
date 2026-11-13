/// <summary>
/// Names the step that runs when no step is named on the command line.
/// </summary>
/// <remarks>
/// Put it on the build class and name the step with <c>nameof</c>, so the compiler checks it:
/// <code>
/// [Entry(nameof(Test))]
/// partial class Build
/// {
///     Step Test() => _ => _
///         .Executes(() => { });
/// }
/// </code>
/// baun reports a compiler error if the name doesn't belong to a step of the class.
/// </remarks>
/// <param name="step">The name of the entry step, for example <c>nameof(Test)</c>.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EntryAttribute(string step) : Attribute
{
    /// <summary>
    /// Gets the name of the entry step.
    /// </summary>
    public string StepName { get; } = step;
}
