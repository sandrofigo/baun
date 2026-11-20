using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Baun;

/// <summary>
/// Configures a <see cref="Step"/>.
/// </summary>
public sealed class StepBuilder
{
    private readonly List<Func<Step>> dependencies = [];
    private readonly List<Func<Task>> actions = [];
    private readonly List<StepRequirement> requirements = [];

    internal StepBuilder()
    {
    }

    internal IReadOnlyList<Func<Step>> Dependencies => dependencies;

    internal IReadOnlyList<Func<Task>> Actions => actions;

    internal IReadOnlyList<StepRequirement> Requirements => requirements;

    /// <summary>
    /// Declares a value this step needs, usually a parameter that isn't needed by every run, such as an API key.
    /// </summary>
    /// <remarks>
    /// baun checks the requirements of every step it is going to run, including dependencies, after it sets the
    /// parameters and before the first step starts. If a value is <see langword="null"/>, <see langword="false"/>, an
    /// empty string or an empty array, the build fails without running any step, so it never stops halfway. Because of that, a requirement can't
    /// be met by an earlier step of the same run: check values that a step computes in the step that uses them.
    /// <code>
    /// Step Publish() => _ => _
    ///     .DependsOn(Pack)
    ///     .Requires(() => NuGetApiKey)
    ///     .Executes(() => ...);
    /// </code>
    /// Chain the call to require several values. For a parameter that every run needs, make it non-nullable without an initial value instead.
    /// </remarks>
    /// <param name="value">Returns the value, for example <c>() => NuGetApiKey</c>.</param>
    /// <param name="expression">The expression of <paramref name="value"/>, filled in by the compiler.</param>
    /// <returns>This builder, to chain further calls.</returns>
    public StepBuilder Requires(Func<object?> value, [CallerArgumentExpression(nameof(value))] string expression = "")
    {
        ArgumentNullException.ThrowIfNull(value);

        requirements.Add(new StepRequirement(value, expression));

        return this;
    }

    /// <summary>
    /// Declares steps that must run before this step.
    /// </summary>
    /// <param name="steps">The steps this step depends on, for example <c>DependsOn(Clean, Restore)</c>.</param>
    /// <returns>This builder, to chain further calls.</returns>
    public StepBuilder DependsOn(params Func<Step>[] steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        foreach (var step in steps)
        {
            ArgumentNullException.ThrowIfNull(step, nameof(steps));
            dependencies.Add(step);
        }

        return this;
    }

    /// <summary>
    /// Sets the work this step does.
    /// </summary>
    /// <param name="action">The work to do.</param>
    /// <returns>This builder, to chain further calls.</returns>
    public StepBuilder Executes(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        actions.Add(() =>
        {
            action();
            return Task.CompletedTask;
        });

        return this;
    }

    /// <summary>
    /// Sets the asynchronous work this step does.
    /// </summary>
    /// <param name="action">The work to do.</param>
    /// <returns>This builder, to chain further calls.</returns>
    public StepBuilder Executes(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        actions.Add(action);

        return this;
    }
}

/// <summary>
/// A value a step needs, declared with <see cref="StepBuilder.Requires"/>.
/// </summary>
/// <param name="Value">Returns the value.</param>
/// <param name="Expression">The expression of <paramref name="Value"/>, such as <c>() => NuGetApiKey</c>.</param>
internal sealed partial record StepRequirement(Func<object?> Value, string Expression)
{
    /// <summary>
    /// Gets the member the expression reads, such as <c>NuGetApiKey</c> for <c>() => NuGetApiKey</c> or
    /// <c>() => this.NuGetApiKey!</c>, or <see langword="null"/> if it does more than read one member.
    /// </summary>
    public string? MemberName { get; } = MemberPattern().Match(Expression) is { Success: true } match ? match.Groups["name"].Value : null;

    /// <summary>
    /// Gets the expression without the lambda, such as <c>NuGetApiKey ?? Fallback</c> for <c>() => NuGetApiKey ?? Fallback</c>.
    /// </summary>
    public string Body { get; } = LambdaPattern().Replace(Expression, string.Empty).Trim();

    [GeneratedRegex(@"^\s*\(\s*\)\s*=>\s*(this\s*\.\s*)?@?(?<name>[\p{L}_][\p{L}\p{Nd}_]*)\s*!?\s*$")]
    private static partial Regex MemberPattern();

    [GeneratedRegex(@"^\s*\(\s*\)\s*=>")]
    private static partial Regex LambdaPattern();
}
