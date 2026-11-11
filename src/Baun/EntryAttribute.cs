/// <summary>
/// Marks the step that runs when no step is named on the command line.
/// </summary>
/// <remarks>
/// A build can have at most one entry step:
/// <code>
/// [Entry]
/// Step Test() => _ => _
///     .DependsOn(Compile)
///     .Executes(() => { });
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class EntryAttribute : Attribute
{
}
