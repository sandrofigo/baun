namespace Baun;

/// <summary>
/// Defines a step of a build.
/// </summary>
/// <remarks>
/// A step is a method on a build class that returns a <see cref="Step"/>. The method's name is the step's name.
/// The returned lambda receives a <see cref="StepBuilder"/> and configures the step on it:
/// <code>
/// Step Compile() => _ => _
///     .DependsOn(Clean)
///     .Executes(() => { });
/// </code>
/// </remarks>
/// <param name="step">The builder that configures the step.</param>
/// <returns>The configured builder.</returns>
public delegate StepBuilder Step(StepBuilder step);
