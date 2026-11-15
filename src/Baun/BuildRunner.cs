using System.Diagnostics;
using System.Reflection;
using Baun.CI;
using Spectre.Console;

namespace Baun;

/// <summary>
/// Resolves the steps of a build and runs them in dependency order.
/// </summary>
/// <param name="build">The build to run.</param>
/// <param name="console">The console to write to.</param>
/// <param name="parameterSources">
/// Where parameter values come from besides the command line. Defaults to the environment variables of this process and
/// <c>.baun/parameters.json</c> in the nearest <c>.baun</c> folder of the current directory or its parents.
/// </param>
/// <param name="ciProvider">
/// The CI server whose log groups each step is written in. Defaults to the one detected from the environment variables.
/// </param>
internal sealed class BuildRunner(
    IBaunBuild build,
    IAnsiConsole console,
    ParameterSources? parameterSources = null,
    CIProvider? ciProvider = null)
{
    private readonly CIProvider provider = ciProvider ?? CIProviders.Detect();

    /// <summary>
    /// Runs the build.
    /// </summary>
    /// <param name="arguments">The command-line arguments, without the program name.</param>
    /// <returns>The process exit code: 0 on success, 1 on failure.</returns>
    public async Task<int> RunAsync(IReadOnlyList<string> arguments)
    {
        if (HasOption(arguments, "--version", "-v"))
        {
            console.WriteLine(GetVersion());
            return 0;
        }

        List<ResolvedStep> plan;

        try
        {
            if (HasOption(arguments, "--help", "-h"))
            {
                // Parameters aren't set for the help, so an invalid value can't hide it.
                WriteHelp(ResolveSteps());
                return 0;
            }

            var parameters = build.GetParameters();
            var (names, parameterValues) = ParseArguments(arguments, parameters);

            // Parameters are set before the steps are resolved, so step definitions can already use them.
            ParameterResolver.Apply(parameters, parameterValues, parameterSources ?? ParameterSources.Default());

            var steps = ResolveSteps();
            var requested = SelectRequestedSteps(steps, names);
            plan = CreateExecutionPlan(requested);
        }
        catch (BuildConfigurationException exception)
        {
            console.WriteError(Markup.Escape(exception.Message));
            return 1;
        }

        var results = new List<StepResult>();
        var total = Stopwatch.StartNew();

        foreach (ResolvedStep step in plan)
        {
            var stopwatch = Stopwatch.StartNew();

            // Outside CI, the step name gets an empty line before and after it. In CI, the log groups separate the steps.
            IDisposable group = LogGroups.Begin(
                console,
                provider,
                step.Name,
                Theme.Faint($"{console.Marker()} {step.Name}"),
                padFallback: true);

            try
            {
                foreach (var action in step.Builder.Actions)
                {
                    await action();
                }
            }
            catch (Exception exception)
            {
                // End the group first, so the error isn't hidden in a collapsed group.
                group.Dispose();

                results.Add(new StepResult(step.Name, StepStatus.Failed, stopwatch.Elapsed));
                console.WriteException(exception, Theme.ExceptionSettings(ExceptionFormats.ShortenPaths));
                results.AddRange(plan.Skip(results.Count).Select(skipped => new StepResult(skipped.Name, StepStatus.Skipped, null)));

                WriteSummary(results);
                console.MarkupLine($"[red]{console.Cross()} Build failed after {Format(total.Elapsed)}[/]");
                return 1;
            }

            group.Dispose();
            results.Add(new StepResult(step.Name, StepStatus.Succeeded, stopwatch.Elapsed));
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

    private static bool HasOption(IReadOnlyList<string> arguments, string name, string shortName) =>
        arguments.Any(argument =>
            string.Equals(argument, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, shortName, StringComparison.Ordinal));

    /// <summary>
    /// Writes the usage, the steps and the options, in the style of the baun tool's help.
    /// </summary>
    private void WriteHelp(List<ResolvedStep> steps)
    {
        Grid Section() => new Grid()
            .AddColumn(new GridColumn().PadLeft(4).PadRight(4).NoWrap())
            .AddColumn();

        Grid stepRows = Section();

        foreach (ResolvedStep step in steps)
        {
            stepRows.AddRow(Markup.Escape(step.Name), step.IsEntry ? "Runs when no step is named" : string.Empty);
        }

        var parameters = build.GetParameters();
        Grid parameterRows = Section();

        foreach (BaunParameter parameter in parameters)
        {
            string option = ParameterResolver.ToOption(parameter.Name);
            string usage = ParameterResolver.IsArray(parameter) ? $"{option} <value>..."
                : ParameterResolver.IsFlag(parameter) ? option
                : $"{option} <value>";

            parameterRows.AddRow("    " + Markup.Escape(usage), Markup.Escape(parameter.Description ?? string.Empty));
        }

        Grid optionRows = Section()
            .AddRow("-h, --help", "Prints help information")
            .AddRow("-v, --version", "Prints version information")
            .AddRow("    --step <name>", "Runs a step whose name is also a command of the baun tool, such as init");

        console.MarkupLine(Theme.Accent("USAGE:"));
        console.MarkupLine($"    baun {Theme.Faint("[steps] [OPTIONS]")}");
        console.WriteLine();
        console.MarkupLine(Theme.Accent("STEPS:"));
        console.Write(stepRows);
        console.WriteLine();

        if (parameters.Count > 0)
        {
            console.MarkupLine(Theme.Accent("PARAMETERS:"));
            console.Write(parameterRows);
            console.WriteLine();
            console.WriteLine(
                $"    Also read from environment variables, such as {ToEnvironmentVariable(parameters[0].Name)}, " +
                $"and {ParameterResolver.ParametersFileDisplayName}.");
            console.WriteLine();
        }

        console.MarkupLine(Theme.Accent("OPTIONS:"));
        console.Write(optionRows);
    }

    /// <summary>
    /// Gets the version of baun the build uses.
    /// </summary>
    private static string GetVersion()
    {
        string version = typeof(BuildRunner).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        // Drop build metadata such as "+3a69508".
        int metadata = version.IndexOf('+');
        return metadata >= 0 ? version[..metadata] : version;
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

    /// <summary>
    /// Returns the environment variable of a parameter, such as <c>NU_GET_API_KEY</c> for <c>NuGetApiKey</c>.
    /// </summary>
    private static string ToEnvironmentVariable(string name) =>
        ParameterResolver.ToOption(name)[2..].Replace('-', '_').ToUpperInvariant();

    /// <summary>
    /// Splits the command-line arguments into step names and parameter values.
    /// </summary>
    private static (List<string> StepNames, Dictionary<BaunParameter, List<string>> ParameterValues) ParseArguments(
        IReadOnlyList<string> arguments,
        IReadOnlyList<BaunParameter> parameters)
    {
        var names = new List<string>();
        var values = new Dictionary<BaunParameter, List<string>>();
        var byName = parameters.ToDictionary(parameter => ParameterResolver.Normalize(parameter.Name));

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
            else if (argument.StartsWith("--", StringComparison.Ordinal) && argument.Length > 2)
            {
                int equals = argument.IndexOf('=');
                string option = equals >= 0 ? argument[..equals] : argument;

                if (!byName.TryGetValue(ParameterResolver.Normalize(option[2..]), out BaunParameter? parameter))
                {
                    throw new BuildConfigurationException($"Unknown option {option}.");
                }

                string value;

                if (equals >= 0)
                {
                    value = argument[(equals + 1)..];
                }
                else if (ParameterResolver.IsFlag(parameter))
                {
                    // --verbose alone means true. The next argument is only its value if it's true or false, so
                    // --verbose Test still runs the step Test.
                    value = i + 1 < arguments.Count && bool.TryParse(arguments[i + 1], out _) ? arguments[++i] : "true";
                }
                else if (i + 1 < arguments.Count && !arguments[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = arguments[++i];
                }
                else
                {
                    throw new BuildConfigurationException($"Option {option} requires a value.");
                }

                if (!values.TryGetValue(parameter, out List<string>? list))
                {
                    values[parameter] = list = [];
                }
                else if (!ParameterResolver.IsArray(parameter))
                {
                    throw new BuildConfigurationException($"Option {option} can only be given once.");
                }

                list.Add(value);
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

        return (names, values);
    }

    private List<ResolvedStep> SelectRequestedSteps(List<ResolvedStep> steps, List<string> names)
    {
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

    private enum StepStatus
    {
        Succeeded,
        Failed,
        Skipped
    }

    private sealed record StepResult(string Name, StepStatus Status, TimeSpan? Duration);
}
