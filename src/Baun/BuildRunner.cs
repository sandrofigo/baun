using System.Diagnostics;
using System.Reflection;
using Spectre.Console;

/// <summary>
/// Resolves the steps of a build and runs them in dependency order.
/// </summary>
internal sealed class BuildRunner
{
    private readonly IBaunBuild build;
    private readonly IAnsiConsole console;

    public BuildRunner(IBaunBuild build, IAnsiConsole console)
    {
        this.build = build;
        this.console = console;
    }

    /// <summary>
    /// Runs the build.
    /// </summary>
    /// <param name="arguments">The command-line arguments, without the program name.</param>
    /// <returns>The process exit code: 0 on success, 1 on failure.</returns>
    public async Task<int> RunAsync(IReadOnlyList<string> arguments)
    {
        List<ResolvedStep> plan;

        try
        {
            var steps = ResolveSteps();
            var requested = SelectRequestedSteps(steps, arguments);
            plan = CreateExecutionPlan(requested);
        }
        catch (BuildConfigurationException exception)
        {
            console.MarkupLineInterpolated($"[red bold]error:[/] {exception.Message}");
            return 1;
        }

        console.MarkupLineInterpolated($"[bold]Running[/] {string.Join(" -> ", plan.Select(step => step.Name))}");

        var results = new List<StepResult>();
        var total = Stopwatch.StartNew();

        foreach (var step in plan)
        {
            console.WriteLine();
            console.Write(new Rule($"[bold]{Markup.Escape(step.Name)}[/]").LeftJustified().RuleStyle("grey"));

            var stopwatch = Stopwatch.StartNew();

            try
            {
                foreach (var action in step.Builder.Actions)
                {
                    await action();
                }
            }
            catch (Exception exception)
            {
                var failedAfter = stopwatch.Elapsed;
                console.WriteException(exception, ExceptionFormats.ShortenPaths);
                console.MarkupLineInterpolated($"[red]Step {step.Name} failed after {Format(failedAfter)}[/]");
                results.Add(new StepResult(step.Name, StepStatus.Failed, failedAfter));
                results.AddRange(plan.Skip(results.Count).Select(skipped => new StepResult(skipped.Name, StepStatus.Skipped, null)));

                WriteSummary(results);
                console.MarkupLineInterpolated($"[red bold]Build failed after {Format(total.Elapsed)}[/]");
                return 1;
            }

            var succeededIn = stopwatch.Elapsed;
            console.MarkupLineInterpolated($"[green]Step {step.Name} succeeded in {Format(succeededIn)}[/]");
            results.Add(new StepResult(step.Name, StepStatus.Succeeded, succeededIn));
        }

        WriteSummary(results);
        console.MarkupLineInterpolated($"[green bold]Build succeeded in {Format(total.Elapsed)}[/]");
        return 0;
    }

    private void WriteSummary(List<StepResult> results)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn("Step")
            .AddColumn("Status")
            .AddColumn(new TableColumn("Duration").RightAligned());

        foreach (var result in results)
        {
            var status = result.Status switch
            {
                StepStatus.Succeeded => "[green]Succeeded[/]",
                StepStatus.Failed => "[red]Failed[/]",
                _ => "[grey]Skipped[/]",
            };

            table.AddRow(
                Markup.Escape(result.Name),
                status,
                result.Duration is { } duration ? Format(duration) : "[grey]-[/]");
        }

        console.WriteLine();
        console.Write(table);
    }

    private List<ResolvedStep> ResolveSteps()
    {
        var steps = new List<ResolvedStep>();

        foreach (var registration in build.GetSteps())
        {
            var builder = new StepBuilder();
            var configured = registration.Definition()(builder) ?? builder;
            steps.Add(new ResolvedStep(registration.Name, registration.Definition.Method, registration.IsEntry, configured));
        }

        var byMethod = steps.ToDictionary(step => step.Method);

        foreach (var step in steps)
        {
            foreach (var dependency in step.Builder.Dependencies)
            {
                if (!byMethod.TryGetValue(dependency.Method, out var resolved))
                {
                    throw new BuildConfigurationException(
                        $"Step {step.Name} depends on {dependency.Method.Name}, which is not a step of {build.GetType().Name}.");
                }

                if (!step.Dependencies.Contains(resolved))
                {
                    step.Dependencies.Add(resolved);
                }
            }
        }

        return steps;
    }

    private List<ResolvedStep> SelectRequestedSteps(List<ResolvedStep> steps, IReadOnlyList<string> arguments)
    {
        var options = arguments.Where(argument => argument.StartsWith('-')).ToList();

        if (options.Count > 0)
        {
            throw new BuildConfigurationException($"Unknown option {options[0]}.");
        }

        if (arguments.Count == 0)
        {
            var step = steps.FirstOrDefault(step => step.IsEntry)
                ?? throw new BuildConfigurationException(
                    $"No step was named and {build.GetType().Name} has no entry step. Name a step, or add [Entry(nameof(...))] to the build class. Available steps: {string.Join(", ", steps.Select(step => step.Name))}.");

            return [step];
        }

        var requested = new List<ResolvedStep>();

        foreach (var name in arguments)
        {
            var step = steps.FirstOrDefault(step => string.Equals(step.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new BuildConfigurationException(
                    $"Unknown step {name}. Available steps: {string.Join(", ", steps.Select(step => step.Name))}.");

            requested.Add(step);
        }

        return requested;
    }

    private static List<ResolvedStep> CreateExecutionPlan(List<ResolvedStep> requested)
    {
        var plan = new List<ResolvedStep>();
        var visited = new HashSet<ResolvedStep>();
        var path = new List<ResolvedStep>();

        foreach (var step in requested)
        {
            Visit(step);
        }

        return plan;

        void Visit(ResolvedStep step)
        {
            if (visited.Contains(step))
            {
                return;
            }

            var index = path.IndexOf(step);

            if (index >= 0)
            {
                var cycle = path.Skip(index).Append(step).Select(item => item.Name);
                throw new BuildConfigurationException($"Circular dependency: {string.Join(" -> ", cycle)}.");
            }

            path.Add(step);

            foreach (var dependency in step.Dependencies)
            {
                Visit(dependency);
            }

            path.RemoveAt(path.Count - 1);
            visited.Add(step);
            plan.Add(step);
        }
    }

    private static string Format(TimeSpan duration) =>
        duration.TotalSeconds < 1
            ? $"{duration.TotalMilliseconds:0} ms"
            : $"{duration.TotalSeconds:0.0} s";

    private sealed class ResolvedStep(string name, MethodInfo method, bool isEntry, StepBuilder builder)
    {
        public string Name { get; } = name;

        public MethodInfo Method { get; } = method;

        public bool IsEntry { get; } = isEntry;

        public StepBuilder Builder { get; } = builder;

        public List<ResolvedStep> Dependencies { get; } = [];
    }

    private sealed class BuildConfigurationException(string message) : Exception(message);

    private enum StepStatus
    {
        Succeeded,
        Failed,
        Skipped,
    }

    private sealed record StepResult(string Name, StepStatus Status, TimeSpan? Duration);
}
