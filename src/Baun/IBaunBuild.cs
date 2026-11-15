using System.ComponentModel;

namespace Baun;

/// <summary>
/// A build class whose steps baun can run.
/// </summary>
/// <remarks>
/// You don't implement this interface yourself. The baun source generator implements it for every
/// <c>partial</c> class that contains steps.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IBaunBuild
{
    /// <summary>
    /// Gets all steps of the build, in the order they are declared.
    /// </summary>
    IReadOnlyList<BaunStep> GetSteps();

    /// <summary>
    /// Gets all parameters of the build, that is its fields and properties marked with <see cref="ParameterAttribute"/>.
    /// </summary>
    IReadOnlyList<BaunParameter> GetParameters();
}

/// <summary>
/// A step registered by the baun source generator.
/// </summary>
/// <param name="Name">The step's name, which is the name of the method that defines it.</param>
/// <param name="Definition">The method that defines the step.</param>
/// <param name="IsEntry">Whether the step is marked with <see cref="EntryAttribute"/>.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record BaunStep(string Name, Func<Step> Definition, bool IsEntry = false);

/// <summary>
/// A parameter registered by the baun source generator.
/// </summary>
/// <param name="Name">The parameter's name: the <see cref="ParameterAttribute.Name"/>, or the name of the field or property.</param>
/// <param name="Type">The type of the field or property.</param>
/// <param name="Description">The description shown in the build's help, if any.</param>
/// <param name="Set">Sets the field or property to a value of <paramref name="Type"/>.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record BaunParameter(string Name, Type Type, string? Description, Action<object?> Set);
