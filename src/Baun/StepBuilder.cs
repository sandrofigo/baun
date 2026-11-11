/// <summary>
/// Configures a <see cref="Step"/>.
/// </summary>
public sealed class StepBuilder
{
    private readonly List<Func<Step>> dependencies = [];
    private readonly List<Func<Task>> actions = [];

    internal StepBuilder()
    {
    }

    internal IReadOnlyList<Func<Step>> Dependencies => dependencies;

    internal IReadOnlyList<Func<Task>> Actions => actions;

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
