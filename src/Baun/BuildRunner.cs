using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Baun.CI;
using Baun.Logging;
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

        ParameterSources sources = parameterSources ?? ParameterSources.Default();

        // The verbosity is read first, so every message of the run, including errors in the arguments, is logged with it.
        LogLevel verbosity = Log.DefaultLevel;
        BuildConfigurationException? verbosityError = null;

        try
        {
            (arguments, verbosity) = ReadVerbosity(arguments, sources);
        }
        catch (BuildConfigurationException exception)
        {
            verbosityError = exception;
        }

        using SerilogBackend backend = SerilogBackend.ForConsole(console, verbosity);
        using IDisposable logScope = Log.Use(backend);

        return await RunAsync(arguments, sources, verbosityError);
    }

    private async Task<int> RunAsync(IReadOnlyList<string> arguments, ParameterSources sources, BuildConfigurationException? verbosityError)
    {
        List<ResolvedStep> plan;

        try
        {
            if (verbosityError is not null)
            {
                throw verbosityError;
            }

            if (HasOption(arguments, "--help", "-h"))
            {
                // Parameters aren't set for the help, so an invalid value can't hide it.
                WriteHelp(ResolveSteps());
                return 0;
            }

            var parameters = build.GetParameters();

            foreach (string option in ReservedOptions)
            {
                if (parameters.FirstOrDefault(parameter => ParameterResolver.Normalize(parameter.Name) == ParameterResolver.Normalize(option)) is { } reserved)
                {
                    throw new BuildConfigurationException(
                        $"Parameter {reserved.Name} can't be used, because --{option} is an option of baun. Give it another name with [Parameter(Name = ...)].");
                }
            }

            bool showPlan = HasOption(arguments, PlanOption, PlanOption);

            if (showPlan)
            {
                arguments = [.. arguments.Where(argument => !string.Equals(argument, PlanOption, StringComparison.OrdinalIgnoreCase))];
            }

            var (names, parameterValues) = ParseArguments(arguments, parameters);

            // Parameters are set before the steps are resolved, so step definitions can already use them.
            ParameterResolver.Apply(parameters, parameterValues, sources);

            var steps = ResolveSteps();
            var requested = SelectRequestedSteps(steps, names);
            plan = CreateExecutionPlan(requested);

            // Checked for the whole plan before the first step starts, so a missing value can't stop the build halfway.
            CheckRequirements(plan, parameters);

            if (showPlan)
            {
                WritePlan(plan);
                return 0;
            }
        }
        catch (BuildConfigurationException exception)
        {
            // Each line is its own message, such as each missing requirement.
            foreach (string line in exception.Message.ReplaceLineEndings("\n").Split('\n'))
            {
                Log.WriteText(LogLevel.Error, line);
            }

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
                $" {Markup.Escape(step.Name)}\n{Theme.Faint(console.Underline($" {step.Name}"))}",
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
                Log.Error(exception, "Step {Step} failed", step.Name);
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

    /// <summary>
    /// The option that prints the execution plan instead of running it.
    /// </summary>
    private const string PlanOption = "--plan";

    /// <summary>
    /// The options of baun that a parameter can't use as its name, without the leading <c>--</c>.
    /// </summary>
    private static readonly string[] ReservedOptions = ["verbosity", "plan"];

    /// <summary>
    /// Writes the steps of <paramref name="plan"/> in the order they run, each with the steps it depends on.
    /// </summary>
    private void WritePlan(List<ResolvedStep> plan)
    {
        Grid rows = new Grid()
            .AddColumn(new GridColumn().PadLeft(4).PadRight(2).RightAligned().NoWrap())
            .AddColumn(new GridColumn().PadRight(4).NoWrap())
            .AddColumn();

        for (int i = 0; i < plan.Count; i++)
        {
            ResolvedStep step = plan[i];
            string dependencies = step.Dependencies.Count > 0
                ? Theme.Faint($"Depends on {string.Join(", ", step.Dependencies.Select(dependency => dependency.Name))}")
                : string.Empty;

            rows.AddRow(Theme.Faint($"{i + 1}."), Markup.Escape(step.Name), dependencies);
        }

        console.MarkupLine(Theme.Accent("PLAN:"));
        console.Write(rows);
    }

    private void WriteSummary(List<StepResult> results)
    {
        Table table = new Table()
            .Border(TableBorder.Simple)
            .BorderStyle(Theme.FaintStyle)
            .AddColumn(new TableColumn(Theme.Faint("Step")).PadLeft(0))
            .AddColumn(new TableColumn(Theme.Faint("Status")))
            .AddColumn(new TableColumn(Theme.Faint("Duration")).RightAligned().PadRight(0));

        foreach (StepResult result in results)
        {
            string status = result.Status switch
            {
                StepStatus.Succeeded => "[green]Succeeded[/]",
                StepStatus.Failed => "[red]Failed[/]",
                _ => Theme.Faint("Skipped")
            };

            table.AddRow(
                result.Status == StepStatus.Skipped ? Theme.Faint(result.Name) : Markup.Escape(result.Name),
                status,
                result.Duration is { } duration ? Format(duration) : Theme.Faint("-"));
        }

        // The simple border starts with an empty line, which separates the table from the step output.
        console.Write(table);
    }

    /// <summary>
    /// The environment variable that sets the verbosity when <c>--verbosity</c> isn't passed.
    /// </summary>
    internal const string VerbosityVariable = "BAUN_VERBOSITY";

    /// <summary>
    /// Reads the minimum log level from <c>--verbosity &lt;level&gt;</c> or <c>--verbosity=&lt;level&gt;</c>, or else from
    /// <see cref="VerbosityVariable"/>, and returns the arguments without the option.
    /// </summary>
    private static (IReadOnlyList<string> Arguments, LogLevel Verbosity) ReadVerbosity(IReadOnlyList<string> arguments, ParameterSources sources)
    {
        var remaining = new List<string>(arguments.Count);
        string? value = null;

        for (int i = 0; i < arguments.Count; i++)
        {
            string argument = arguments[i];

            if (string.Equals(argument, "--verbosity", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= arguments.Count || arguments[i + 1].StartsWith('-'))
                {
                    throw new BuildConfigurationException("Option --verbosity requires a level: Verbose, Debug, Information, Warning, Error or Fatal.");
                }

                value = arguments[++i];
            }
            else if (argument.StartsWith("--verbosity=", StringComparison.OrdinalIgnoreCase))
            {
                value = argument["--verbosity=".Length..];
            }
            else
            {
                remaining.Add(argument);
            }
        }

        if (value is null)
        {
            if (!sources.Environment.TryGetValue(VerbosityVariable, out value) || string.IsNullOrWhiteSpace(value))
            {
                return (remaining, Log.DefaultLevel);
            }
        }

        // Only names are accepted, so a number such as 3 isn't taken as a level.
        string name = Enum.GetNames<LogLevel>().FirstOrDefault(name => string.Equals(name, value.Trim(), StringComparison.OrdinalIgnoreCase))
                      ?? throw new BuildConfigurationException($"Unknown verbosity {value}. Use Verbose, Debug, Information, Warning, Error or Fatal.");

        return (remaining, Enum.Parse<LogLevel>(name));
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

        var parameters = build.GetParameters();

        foreach (ResolvedStep step in steps)
        {
            var notes = new List<string>();

            if (step.IsEntry)
            {
                notes.Add("Runs when no step is named");
            }

            if (step.Builder.Requirements.Count > 0)
            {
                var required = step.Builder.Requirements.Select(requirement =>
                    FindParameter(requirement, parameters) is { } parameter ? ParameterResolver.ToOption(parameter.Name) : requirement.Body);

                notes.Add($"Requires {string.Join(", ", required)}");
            }

            stepRows.AddRow(Markup.Escape(step.Name), Markup.Escape(string.Join("; ", notes)));
        }

        Grid parameterRows = Section();

        foreach (BaunParameter parameter in parameters)
        {
            string option = ParameterResolver.ToOption(parameter.Name);
            string usage = ParameterResolver.IsArray(parameter) ? $"{option} <value>..."
                : ParameterResolver.IsFlag(parameter) ? option
                : $"{option} <value>";

            string description = Markup.Escape(parameter.Description ?? string.Empty);

            if (parameter.IsRequired)
            {
                description = $"{description} {Theme.Faint("(required)")}".TrimStart();
            }

            parameterRows.AddRow("    " + Markup.Escape(usage), description);
        }

        Grid optionRows = Section()
            .AddRow("-h, --help", "Prints help information")
            .AddRow("-v, --version", "Prints version information")
            .AddRow("    --plan", "Prints the steps that would run, in order, without running them")
            .AddRow("    --step <name>", "Runs a step whose name is also a command of the baun tool, such as init")
            .AddRow(
                "    --verbosity <level>",
                $"Sets the minimum log level: Verbose, Debug (default), Information, Warning, Error or Fatal. Also read from {VerbosityVariable}");

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
                $"    Also read from environment variables, such as {ParameterResolver.ToEnvironmentVariable(parameters[0].Name)}, " +
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
    /// Throws if a step of <paramref name="plan"/> requires a value that isn't set, naming every missing value.
    /// </summary>
    private static void CheckRequirements(List<ResolvedStep> plan, IReadOnlyList<BaunParameter> parameters)
    {
        var missing = new List<string>();

        foreach (ResolvedStep step in plan)
        {
            foreach (StepRequirement requirement in step.Builder.Requirements)
            {
                object? value;

                try
                {
                    value = requirement.Value();
                }
                catch (Exception exception)
                {
                    throw new BuildConfigurationException($"Step {step.Name} requires {requirement.Body}, which failed: {exception.Message}");
                }

                if (!ParameterResolver.IsMissing(value))
                {
                    continue;
                }

                missing.Add(FindParameter(requirement, parameters) is { } parameter
                    ? $"Step {step.Name} requires parameter {parameter.Name}. {ParameterResolver.DescribeSources(parameter)}"
                    : $"Step {step.Name} requires {requirement.Body}, which isn't set.");
            }
        }

        if (missing.Count > 0)
        {
            throw new BuildConfigurationException(string.Join(Environment.NewLine, missing));
        }
    }

    /// <summary>
    /// Returns the parameter <paramref name="requirement"/> reads, or <see langword="null"/> if it reads something else.
    /// </summary>
    private static BaunParameter? FindParameter(StepRequirement requirement, IReadOnlyList<BaunParameter> parameters) =>
        requirement.MemberName is { } name
            ? parameters.FirstOrDefault(parameter => parameter.MemberName == name)
            : null;

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

    /// <summary>
    /// Returns <paramref name="duration"/> in whole seconds, rounded down, or <c>&lt; 1s</c>.
    /// </summary>
    internal static string Format(TimeSpan duration) =>
        duration.TotalSeconds < 1
            ? "< 1s"
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Floor(duration.TotalSeconds):0}s");

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
