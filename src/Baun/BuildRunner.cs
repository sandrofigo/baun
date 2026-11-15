using System.Diagnostics;
using System.Reflection;
using Spectre.Console;

namespace Baun;

/// <summary>
/// Resolves the steps of a build and runs them in dependency order.
/// </summary>
internal sealed class BuildRunner(IBaunBuild build, IAnsiConsole console)
{
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
            console.WriteError(Markup.Escape(exception.Message));
            return 1;
        }

        string arrow = $" {Theme.Faint(console.Arrow())} ";
        console.MarkupLine($"{Theme.Accent("baun")}  {string.Join(arrow, plan.Select(step => Markup.Escape(step.Name)))}");

        var results = new List<StepResult>();
        var total = Stopwatch.StartNew();

        foreach (ResolvedStep step in plan)
        {
            console.WriteLine();
            console.Write(new Rule(Theme.Accent(step.Name)).LeftJustified().RuleStyle(Theme.FaintStyle));

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
                TimeSpan failedAfter = stopwatch.Elapsed;
                console.WriteException(exception, Theme.ExceptionSettings(ExceptionFormats.ShortenPaths));
                console.MarkupLine($"[red]{console.Cross()} {Markup.Escape(step.Name)} failed after {Format(failedAfter)}[/]");
                results.Add(new StepResult(step.Name, StepStatus.Failed, failedAfter));
                results.AddRange(plan.Skip(results.Count).Select(skipped => new StepResult(skipped.Name, StepStatus.Skipped, null)));

                WriteSummary(results);
                console.MarkupLine($"[red]{console.Cross()} Build failed after {Format(total.Elapsed)}[/]");
                return 1;
            }

            TimeSpan succeededIn = stopwatch.Elapsed;
            console.MarkupLine($"[green]{console.Check()}[/] {Markup.Escape(step.Name)} {Theme.Faint(Format(succeededIn))}");
            results.Add(new StepResult(step.Name, StepStatus.Succeeded, succeededIn));
        }

        WriteSummary(results);
        console.MarkupLine($"[green]{console.Check()} Build succeeded in {Format(total.Elapsed)}[/]");
        return 0;
    }

    private void WriteSummary(List<StepResult> results)
    {
        Table table = new Table()
            .Border(TableBorder.Simple)
            .BorderStyle(Theme.FaintStyle)
            .AddColumn(new TableColumn(Theme.Faint("Step")))
            .AddColumn(new TableColumn(Theme.Faint("Status")))
            .AddColumn(new TableColumn(Theme.Faint("Duration")).RightAligned());

        foreach (StepResult result in results)
        {
            string status = result.Status switch
            {
                StepStatus.Succeeded => $"[green]{console.Check()} Succeeded[/]",
                StepStatus.Failed => $"[red]{console.Cross()} Failed[/]",
                _ => Theme.Faint($"{console.Circle()} Skipped")
            };

            table.AddRow(
                result.Status == StepStatus.Skipped ? Theme.Faint(result.Name) : Markup.Escape(result.Name),
                status,
                result.Duration is { } duration ? FormatSummaryDuration(duration) : Theme.Faint("-"));
        }

        // The simple border starts with an empty line, which separates the table from the step output.
        console.Write(table);
    }

    private List<ResolvedStep> ResolveSteps()
    {
        var steps = new List<ResolvedStep>();

        foreach (BaunStep registration in build.GetSteps())
        {
            var builder = new StepBuilder();
            StepBuilder configured = registration.Definition()(builder);
            steps.Add(new ResolvedStep(registration.Name, registration.Definition.Method, registration.IsEntry, configured));
        }

        var byMethod = steps.ToDictionary(step => step.Method);

        foreach (ResolvedStep step in steps)
        {
            foreach (var dependency in step.Builder.Dependencies)
            {
                if (!byMethod.TryGetValue(dependency.Method, out ResolvedStep? resolved))
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
        var names = new List<string>();

        for (int i = 0; i < arguments.Count; i++)
        {
            string argument = arguments[i];

            if (string.Equals(argument, "--step", StringComparison.OrdinalIgnoreCase))
            {
                // --step runs a step whose name is also a command of the baun tool, such as --step Init.
                if (i + 1 >= arguments.Count || arguments[i + 1].StartsWith('-'))
                {
                    throw new BuildConfigurationException("Option --step requires a step name.");
                }

                names.Add(arguments[++i]);
            }
            else if (argument.StartsWith('-'))
            {
                throw new BuildConfigurationException($"Unknown option {argument}.");
            }
            else
            {
                names.Add(argument);
            }
        }

        if (names.Count == 0)
        {
            ResolvedStep step = steps.FirstOrDefault(step => step.IsEntry)
                                ?? throw new BuildConfigurationException(
                                    $"No step was named and {build.GetType().Name} has no entry step. Name a step, or add [Entry(nameof(...))] to the build class. Available steps: {string.Join(", ", steps.Select(step => step.Name))}.");

            return [step];
        }

        var requested = new List<ResolvedStep>();

        foreach (string name in names)
        {
            ResolvedStep step = steps.FirstOrDefault(step => string.Equals(step.Name, name, StringComparison.OrdinalIgnoreCase))
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

        foreach (ResolvedStep step in requested)
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

            int index = path.IndexOf(step);

            if (index >= 0)
            {
                var cycle = path.Skip(index).Append(step).Select(item => item.Name);
                throw new BuildConfigurationException($"Circular dependency: {string.Join(" -> ", cycle)}.");
            }

            path.Add(step);

            foreach (ResolvedStep dependency in step.Dependencies)
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
            ? $"{duration.TotalMilliseconds:0}ms"
            : $"{duration.TotalSeconds:0.0}s";

    private static string FormatSummaryDuration(TimeSpan duration) =>
        duration.TotalSeconds < 1 ? "<1s" : Format(duration);

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
        Skipped
    }

    private sealed record StepResult(string Name, StepStatus Status, TimeSpan? Duration);
}
