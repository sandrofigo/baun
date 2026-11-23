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
    private readonly List<StepCondition> conditions = [];
    private readonly List<Func<Step>> after = [];
    private readonly List<Func<Step>> before = [];
    private readonly List<Func<Step>> triggers = [];
    private readonly List<Func<Step>> triggeredBy = [];

    internal StepBuilder()
    {
    }

    internal IReadOnlyList<Func<Step>> Dependencies => dependencies;

    internal IReadOnlyList<Func<Task>> Actions => actions;

    internal IReadOnlyList<StepRequirement> Requirements => requirements;

    internal IReadOnlyList<StepCondition> Conditions => conditions;

    internal IReadOnlyList<Func<Step>> AfterSteps => after;

    internal IReadOnlyList<Func<Step>> BeforeSteps => before;

    internal IReadOnlyList<Func<Step>> TriggeredSteps => triggers;

    internal IReadOnlyList<Func<Step>> TriggeringSteps => triggeredBy;

    internal bool ProceedsAfterFailure { get; private set; }

    internal bool RunsAlways { get; private set; }

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

        AddSteps(dependencies, steps);

        return this;
    }

    /// <summary>
    /// Runs this step only when a condition is true.
    /// </summary>
    /// <remarks>
    /// baun checks the condition right before the step starts, after its dependencies ran, so it can use values that
    /// earlier steps computed. When it's <see langword="false"/>, the step is skipped, and the steps that depend on it still
    /// run.
    /// <code>
    /// Step Publish() => _ => _
    ///     .DependsOn(Pack)
    ///     .OnlyWhen(() => GitHubActions.IsRunning)
    ///     .Executes(() => ...);
    /// </code>
    /// Chain the call to add several conditions, which must all be true. If a condition throws, the step fails. The
    /// requirements of a step with a condition are checked when its conditions are true, right before it starts.
    /// </remarks>
    /// <param name="condition">Returns whether the step runs, for example <c>() => GitHubActions.IsRunning</c>.</param>
    /// <param name="expression">The expression of <paramref name="condition"/>, filled in by the compiler.</param>
    /// <returns>This builder, to chain further calls.</returns>
    public StepBuilder OnlyWhen(Func<bool> condition, [CallerArgumentExpression(nameof(condition))] string expression = "")
    {
        ArgumentNullException.ThrowIfNull(condition);

        conditions.Add(new StepCondition(condition, expression));

        return this;
    }

    /// <summary>
    /// Declares steps that run before this step when they run too, without making this step depend on them.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="DependsOn"/>, the steps only run when they're named on the command line or another step needs
    /// them. For example, <c>Restore</c> with <c>After(Clean)</c> runs after <c>Clean</c> in <c>baun Clean Restore</c>, but
    /// <c>baun Restore</c> doesn't run <c>Clean</c>.
    /// </remarks>
    /// <param name="steps">The steps that run first, if they run.</param>
    /// <returns>This builder, to chain further calls.</returns>
    public StepBuilder After(params Func<Step>[] steps)
    {
        AddSteps(after, steps);

        return this;
    }

    /// <summary>
    /// Declares steps that run after this step when they run too, without making them depend on this step.
    /// </summary>
    /// <remarks>
    /// This is the reverse of <see cref="After"/>: <c>Clean</c> with <c>Before(Restore)</c> orders the steps the same way
    /// as <c>Restore</c> with <c>After(Clean)</c>.
    /// </remarks>
    /// <param name="steps">The steps that run later, if they run.</param>
    /// <returns>This builder, to chain further calls.</returns>
    public StepBuilder Before(params Func<Step>[] steps)
    {
        AddSteps(before, steps);

        return this;
    }

    /// <summary>
    /// Declares steps that run after this step whenever this step runs.
    /// </summary>
    /// <remarks>
    /// The steps and their dependencies are added to the run after this step, for example a step that sends a notification
    /// after <c>Publish</c>. They don't run when this step isn't part of the run.
    /// </remarks>
    /// <param name="steps">The steps this step triggers.</param>
    /// <returns>This builder, to chain further calls.</returns>
    public StepBuilder Triggers(params Func<Step>[] steps)
    {
        AddSteps(triggers, steps);

        return this;
    }

    /// <summary>
    /// Declares steps that make this step run after them whenever they run.
    /// </summary>
    /// <remarks>
    /// This is the reverse of <see cref="Triggers"/>: <c>Notify</c> with <c>TriggeredBy(Publish)</c> is the same as
    /// <c>Publish</c> with <c>Triggers(Notify)</c>.
    /// </remarks>
    /// <param name="steps">The steps that trigger this step.</param>
    /// <returns>This builder, to chain further calls.</returns>
    public StepBuilder TriggeredBy(params Func<Step>[] steps)
    {
        AddSteps(triggeredBy, steps);

        return this;
    }

    /// <summary>
    /// Lets the build go on when this step fails.
    /// </summary>
    /// <remarks>
    /// The steps that depend on this step, directly or through other steps, don't run, but the other steps do. The build
    /// still fails at the end.
    /// </remarks>
    /// <returns>This builder, to chain further calls.</returns>
    public StepBuilder ProceedAfterFailure()
    {
        ProceedsAfterFailure = true;

        return this;
    }

    /// <summary>
    /// Runs this step even when an earlier step failed, for example to clean up.
    /// </summary>
    /// <remarks>
    /// The step still runs in its place in the order. Its dependencies that didn't run because of the failure aren't run
    /// for it. A condition from <see cref="OnlyWhen"/> still applies.
    /// </remarks>
    /// <returns>This builder, to chain further calls.</returns>
    public StepBuilder AlwaysRuns()
    {
        RunsAlways = true;

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

    private static void AddSteps(List<Func<Step>> list, Func<Step>[] steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        foreach (var step in steps)
        {
            ArgumentNullException.ThrowIfNull(step, nameof(steps));
            list.Add(step);
        }
    }
}

/// <summary>
/// A condition of a step, declared with <see cref="StepBuilder.OnlyWhen"/>.
/// </summary>
/// <param name="Value">Returns whether the step runs.</param>
/// <param name="Expression">The expression of <paramref name="Value"/>, such as <c>() => GitHubActions.IsRunning</c>.</param>
internal sealed record StepCondition(Func<bool> Value, string Expression)
{
    /// <summary>
    /// Gets the expression without the lambda, such as <c>GitHubActions.IsRunning</c>.
    /// </summary>
    public string Body { get; } = StepRequirement.RemoveLambda(Expression);
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
    public string Body { get; } = RemoveLambda(Expression);

    /// <summary>
    /// Returns <paramref name="expression"/> without the <c>() =></c> of the lambda.
    /// </summary>
    internal static string RemoveLambda(string expression) => LambdaPattern().Replace(expression, string.Empty).Trim();

    [GeneratedRegex(@"^\s*\(\s*\)\s*=>\s*(this\s*\.\s*)?@?(?<name>[\p{L}_][\p{L}\p{Nd}_]*)\s*!?\s*$")]
    private static partial Regex MemberPattern();

    [GeneratedRegex(@"^\s*\(\s*\)\s*=>")]
    private static partial Regex LambdaPattern();
}
