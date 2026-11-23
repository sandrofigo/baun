using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Baun.CI;
using Baun.Logging;
using Baun.Plan;
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
/// <param name="openInBrowser">
/// Opens a file in the browser and returns whether it could. Defaults to <see cref="Browser.TryOpen"/>.
/// </param>
/// <param name="planFolder">
/// The folder the plan page is written to. Defaults to the temp directory of baun for the current directory (see <see cref="BaunBuild.TempDirectory"/>).
/// </param>
internal sealed class BuildRunner(
    IBaunBuild build,
    IAnsiConsole console,
    ParameterSources? parameterSources = null,
    CIProvider? ciProvider = null,
    Func<string, bool>? openInBrowser = null,
    AbsolutePath? planFolder = null)
{
    private readonly CIProvider provider = ciProvider ?? CIProviders.Detect();

    private readonly Func<string, bool> openInBrowser = openInBrowser ?? Browser.TryOpen;

    /// <summary>
    /// The normalized names of the parameters, which take over built-in options of the same name.
    /// </summary>
    private readonly HashSet<string> parameterNames = [.. build.GetParameters().Select(parameter => ParameterResolver.Normalize(parameter.Name))];

    /// <summary>
    /// The prefix of the names under which every built-in option stays available, such as <c>--baun-plan</c>.
    /// </summary>
    private const string OptionPrefix = "baun-";

    /// <summary>
    /// Runs the build.
    /// </summary>
    /// <param name="arguments">The command-line arguments, without the program name.</param>
    /// <returns>The process exit code: 0 on success, 1 on failure.</returns>
    public async Task<int> RunAsync(IReadOnlyList<string> arguments)
    {
        if (HasOption(arguments, "version", "-v"))
        {
            console.WriteLine(BaunVersion.Get(typeof(BuildRunner).Assembly));
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
        if (!await TryRunHookAsync<IOnBuildCreated>(hook => hook.OnBuildCreatedAsync()))
        {
            return 1;
        }

        List<ResolvedStep> plan;
        HashSet<ResolvedStep> skipped;
        IReadOnlyList<BaunParameter> parameters;

        try
        {
            if (verbosityError is not null)
            {
                throw verbosityError;
            }

            if (HasOption(arguments, "help", "-h"))
            {
                // Parameters aren't set for the help, so an invalid value can't hide it.
                WriteHelp(ResolveSteps());
                return 0;
            }

            parameters = build.GetParameters();

            IReadOnlyList<string> withoutPlan = [.. arguments.Where(argument => !IsOption(argument, PlanOption))];
            bool showPlan = withoutPlan.Count != arguments.Count;

            var parsed = ParseArguments(withoutPlan, parameters);

            // Parameters are set before the steps are resolved, so step definitions can already use them.
            ParameterResolver.Apply(parameters, parsed.ParameterValues, sources);

            var steps = ResolveSteps();
            var requested = SelectRequestedSteps(steps, parsed.StepNames);
            plan = CreateExecutionPlan(requested);
            skipped = SelectSkippedSteps(steps, plan, requested, parsed.SkipNames, parsed.SkipAll);

            // Checked for the whole plan before the first step starts, so a missing value can't stop the build halfway.
            CheckRequirements(plan, skipped, parameters);

            if (showPlan)
            {
                return ShowPlan(steps, plan, skipped);
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

        bool succeeded = await TryRunHookAsync<IOnBuildInitialized>(hook => hook.OnBuildInitializedAsync())
                         && await RunStepsAsync(plan, skipped, parameters, results);

        if (succeeded)
        {
            // A failing success hook fails the build, but doesn't make it run the failure hook.
            succeeded = await TryRunHookAsync<IOnBuildSucceeded>(hook => hook.OnBuildSucceededAsync());
        }
        else
        {
            await TryRunHookAsync<IOnBuildFailed>(hook => hook.OnBuildFailedAsync());
        }

        succeeded &= await TryRunHookAsync<IOnBuildCompleted>(hook => hook.OnBuildCompletedAsync());

        results.AddRange(plan.Skip(results.Count).Select(skipped => new StepResult(skipped.Name, StepStatus.Skipped, null)));
        WriteSummary(results);

        if (!succeeded)
        {
            console.MarkupLine($"[red]{console.Cross()} Build failed after {Format(total.Elapsed)}[/]");
            return 1;
        }

        console.MarkupLine($"[green]{console.Check()} Build succeeded in {Format(total.Elapsed)}[/]");
        return 0;
    }

    /// <summary>
    /// Runs the steps of <paramref name="plan"/> in order and adds the result of each to <paramref name="results"/>.
    /// </summary>
    /// <remarks>
    /// After a step fails, the remaining steps are skipped, except those that always run. A step that proceeds after a
    /// failure only skips the steps that depend on it.
    /// </remarks>
    /// <returns>Whether every step and its hooks succeeded.</returns>
    private async Task<bool> RunStepsAsync(
        List<ResolvedStep> plan,
        HashSet<ResolvedStep> skipped,
        IReadOnlyList<BaunParameter> parameters,
        List<StepResult> results)
    {
        bool failed = false;
        bool stopped = false;

        // The steps that failed or didn't run because of a failure, whose dependents don't run either.
        var notRun = new HashSet<ResolvedStep>();

        foreach (ResolvedStep step in plan)
        {
            if (skipped.Contains(step))
            {
                results.Add(new StepResult(step.Name, StepStatus.Skipped, null));
                continue;
            }

            if (!step.Builder.RunsAlways && (stopped || step.Dependencies.Any(notRun.Contains)))
            {
                notRun.Add(step);
                results.Add(new StepResult(step.Name, StepStatus.Skipped, null));
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            Exception? failure = null;
            bool run = true;

            try
            {
                run = ShouldRun(step, parameters);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (!run)
            {
                results.Add(new StepResult(step.Name, StepStatus.Skipped, null));
                continue;
            }

            failure ??= await RunStepAsync(step);

            if (failure is not null)
            {
                results.Add(new StepResult(step.Name, StepStatus.Failed, stopwatch.Elapsed));
                LogFailure(step, failure);

                await TryRunHookAsync<IOnStepFailed>(hook => hook.OnStepFailedAsync(step.Name, failure));
                await TryRunHookAsync<IOnStepCompleted>(hook => hook.OnStepCompletedAsync(step.Name));

                failed = true;
                notRun.Add(step);
                stopped |= !step.Builder.ProceedsAfterFailure;
                continue;
            }

            results.Add(new StepResult(step.Name, StepStatus.Succeeded, stopwatch.Elapsed));

            if (!await TryRunHookAsync<IOnStepCompleted>(hook => hook.OnStepCompletedAsync(step.Name)))
            {
                failed = true;
                stopped = true;
            }
        }

        return !failed;
    }

    /// <summary>
    /// Returns whether the conditions of <paramref name="step"/> are true, and logs why it's skipped when one isn't. When
    /// they are, checks the requirements of a step with conditions, which aren't checked before the build starts.
    /// </summary>
    /// <exception cref="BuildConfigurationException">A requirement of the step isn't met.</exception>
    private static bool ShouldRun(ResolvedStep step, IReadOnlyList<BaunParameter> parameters)
    {
        foreach (StepCondition condition in step.Builder.Conditions)
        {
            if (!condition.Value())
            {
                Log.Information("Skipping {Step} because {Condition} is false", step.Name, condition.Body);
                return false;
            }
        }

        if (step.Builder.Conditions.Count > 0)
        {
            var missing = new List<string>();
            AddMissingRequirements(step, parameters, missing);

            if (missing.Count > 0)
            {
                throw new BuildConfigurationException(string.Join(Environment.NewLine, missing));
            }
        }

        return true;
    }

    /// <summary>
    /// Runs the hooks and the work of <paramref name="step"/> in its log group.
    /// </summary>
    /// <returns>The exception that failed the step, or <see langword="null"/> when it succeeded.</returns>
    private async Task<Exception?> RunStepAsync(ResolvedStep step)
    {
        // Outside CI, the step name gets an empty line before and after it. In CI, the log groups separate the steps.
        IDisposable group = LogGroups.Begin(
            console,
            provider,
            step.Name,
            $" {Markup.Escape(step.Name)}\n{Theme.Faint(console.Underline($" {step.Name}"))}",
            padFallback: true);

        try
        {
            // The start and success hooks belong to the step, so an exception in them fails it.
            await RunHookAsync<IOnStepStarted>(hook => hook.OnStepStartedAsync(step.Name));

            foreach (var action in step.Builder.Actions)
            {
                await action();
            }

            await RunHookAsync<IOnStepSucceeded>(hook => hook.OnStepSucceededAsync(step.Name));
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
        finally
        {
            // End the group first, so an error isn't hidden in a collapsed group.
            group.Dispose();
        }
    }

    /// <summary>
    /// Logs that <paramref name="step"/> failed with <paramref name="failure"/>. A missing requirement is logged as its
    /// message only, like before the build starts.
    /// </summary>
    private static void LogFailure(ResolvedStep step, Exception failure)
    {
        if (failure is BuildConfigurationException)
        {
            foreach (string line in failure.Message.ReplaceLineEndings("\n").Split('\n'))
            {
                Log.WriteText(LogLevel.Error, line);
            }

            Log.Error("Step {Step} failed", step.Name);
            return;
        }

        Log.Error(failure, "Step {Step} failed", step.Name);
    }

    /// <summary>
    /// Runs a hook if the build implements <typeparamref name="THook"/>, and lets an exception of it through.
    /// </summary>
    private Task RunHookAsync<THook>(Func<THook, Task> run)
        where THook : class =>
        build is THook hook ? run(hook) : Task.CompletedTask;

    /// <summary>
    /// Runs a hook if the build implements <typeparamref name="THook"/>, and logs an exception of it as an error.
    /// </summary>
    /// <returns>Whether the hook succeeded or the build doesn't implement it.</returns>
    private async Task<bool> TryRunHookAsync<THook>(Func<THook, Task> run)
        where THook : class
    {
        if (build is not THook hook)
        {
            return true;
        }

        try
        {
            await run(hook);
            return true;
        }
        catch (Exception exception)
        {
            // IOnBuildFailed runs the hook OnBuildFailedAsync.
            Log.Error(exception, "Hook {Hook} failed", typeof(THook).Name[1..] + "Async");
            return false;
        }
    }

    /// <summary>
    /// The option that opens the execution plan in the browser instead of running it.
    /// </summary>
    private const string PlanOption = "plan";

    /// <summary>
    /// Opens all <paramref name="steps"/> of the build as a graph in the browser, with the steps of <paramref name="plan"/>
    /// numbered in the order they run. When the browser can't open, writes where the page is instead.
    /// </summary>
    /// <returns>The process exit code: 0 on success, 1 when the page can't be written.</returns>
    private int ShowPlan(List<ResolvedStep> steps, List<ResolvedStep> plan, HashSet<ResolvedStep> skipped)
    {
        AbsolutePath folder = planFolder ?? BaunBuild.FindTempDirectory(AbsolutePath.CurrentDirectory);
        AbsolutePath path;

        try
        {
            var pageSteps = steps
                .Select(step => new PlanStep(
                    step.Name,
                    [.. step.Dependencies.Select(dependency => dependency.Name)],
                    plan.IndexOf(step) is var index and >= 0 ? index + 1 : null)
                {
                    Triggers = [.. step.Triggered.Select(triggered => triggered.Name)],
                    Skipped = skipped.Contains(step),
                    Condition = step.Builder.Conditions.Count > 0
                        ? string.Join(" && ", step.Builder.Conditions.Select(condition => condition.Body))
                        : null
                })
                .ToList();
            path = PlanPage.Write(pageSteps, folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error(exception, "Could not write the plan to {Folder}", folder);
            return 1;
        }

        console.MarkupLine(openInBrowser(path)
            ? $"Opened the plan in the browser {Theme.Faint(path)}"
            : $"Open {Theme.Accent(path)} in a browser to see the plan");
        return 0;
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
    private (IReadOnlyList<string> Arguments, LogLevel Verbosity) ReadVerbosity(IReadOnlyList<string> arguments, ParameterSources sources)
    {
        var remaining = new List<string>(arguments.Count);
        string? value = null;

        for (int i = 0; i < arguments.Count; i++)
        {
            string argument = arguments[i];
            int equals = argument.IndexOf('=');

            if (IsOption(argument, VerbosityOption))
            {
                if (!HasValueAfter(arguments, i))
                {
                    throw new BuildConfigurationException($"Option {argument} requires a level: Verbose, Debug, Information, Warning, Error or Fatal.");
                }

                value = arguments[++i];
            }
            else if (equals >= 0 && IsOption(argument[..equals], VerbosityOption))
            {
                value = argument[(equals + 1)..];
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

    /// <summary>
    /// The option that sets the minimum log level.
    /// </summary>
    private const string VerbosityOption = "verbosity";

    /// <summary>
    /// The option that runs a step whose name is also a command of the baun tool.
    /// </summary>
    private const string StepOption = "step";

    /// <summary>
    /// The option that skips a step of the plan, or every step but the named ones when it has no value.
    /// </summary>
    private const string SkipOption = "skip";

    private bool HasOption(IReadOnlyList<string> arguments, string name, string shortName) =>
        arguments.Any(argument => IsOption(argument, name) || string.Equals(argument, shortName, StringComparison.Ordinal));

    /// <summary>
    /// Returns whether <paramref name="argument"/> is the built-in option <paramref name="name"/>, such as <c>--plan</c>, or
    /// its prefixed form, such as <c>--baun-plan</c>. Like parameters, it matches case-insensitively and ignores <c>-</c> and
    /// <c>_</c>. A parameter of the same name takes over the short form.
    /// </summary>
    private bool IsOption(string argument, string name)
    {
        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        string given = ParameterResolver.Normalize(argument[2..]);
        string option = ParameterResolver.Normalize(name);

        return given == ParameterResolver.Normalize(OptionPrefix + name) || (given == option && !parameterNames.Contains(option));
    }

    /// <summary>
    /// Returns whether the option at <paramref name="index"/> is followed by a value: an argument that isn't an option.
    /// </summary>
    private static bool HasValueAfter(IReadOnlyList<string> arguments, int index) =>
        index + 1 < arguments.Count && !arguments[index + 1].StartsWith('-');

    /// <summary>
    /// Returns how the help shows the built-in option <paramref name="name"/>: <c>--plan</c>, or <c>--baun-plan</c> when a
    /// parameter takes over <c>--plan</c>.
    /// </summary>
    private string ToHelpOption(string name) =>
        parameterNames.Contains(ParameterResolver.Normalize(name)) ? $"--{OptionPrefix}{name}" : $"--{name}";

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

            if (step.Builder.Conditions.Count > 0)
            {
                notes.Add($"Only when {string.Join(" and ", step.Builder.Conditions.Select(condition => condition.Body))}");
            }

            if (step.Triggered.Count > 0)
            {
                notes.Add($"Triggers {string.Join(", ", step.Triggered.Select(triggered => triggered.Name))}");
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
            .AddRow($"    {ToHelpOption(PlanOption)}", "Opens the steps that would run, in order, in the browser without running them")
            .AddRow($"    {ToHelpOption(StepOption)} <name>", "Runs a step whose name is also a command of the baun tool, such as init")
            .AddRow(
                $"    {ToHelpOption(SkipOption)} [[<step>]]",
                "Skips a step that would run, and can be repeated. Without a step, skips all steps but the named ones")
            .AddRow(
                $"    {ToHelpOption(VerbosityOption)} <level>",
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
        console.WriteLine();
        console.WriteLine($"    Each option also works with the prefix --{OptionPrefix}, such as --{OptionPrefix}{PlanOption}.");
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
            foreach (ResolvedStep dependency in Resolve(step, step.Builder.Dependencies, "depends on"))
            {
                AddOnce(step.Dependencies, dependency);
            }

            foreach (ResolvedStep earlier in Resolve(step, step.Builder.AfterSteps, "runs after"))
            {
                AddOnce(step.OrderedAfter, earlier);
            }

            foreach (ResolvedStep later in Resolve(step, step.Builder.BeforeSteps, "runs before"))
            {
                AddOnce(later.OrderedAfter, step);
            }

            foreach (ResolvedStep triggered in Resolve(step, step.Builder.TriggeredSteps, "triggers"))
            {
                AddOnce(step.Triggered, triggered);
            }

            foreach (ResolvedStep trigger in Resolve(step, step.Builder.TriggeringSteps, "is triggered by"))
            {
                AddOnce(trigger.Triggered, step);
            }
        }

        foreach (ResolvedStep step in steps)
        {
            foreach (ResolvedStep triggered in step.Triggered)
            {
                triggered.TriggeredBy.Add(step);
            }
        }

        return steps;

        IEnumerable<ResolvedStep> Resolve(ResolvedStep step, IReadOnlyList<Func<Step>> references, string relation) =>
            references.Select(reference => byMethod.TryGetValue(reference.Method, out ResolvedStep? resolved)
                ? resolved
                : throw new BuildConfigurationException(
                    $"Step {step.Name} {relation} {reference.Method.Name}, which is not a step of {build.GetType().Name}."));

        static void AddOnce(List<ResolvedStep> list, ResolvedStep step)
        {
            if (!list.Contains(step))
            {
                list.Add(step);
            }
        }
    }

    /// <summary>
    /// Throws if a step of <paramref name="plan"/> requires a value that isn't set, naming every missing value.
    /// </summary>
    /// <remarks>
    /// Skipped steps aren't checked. Steps with conditions are checked right before they run, when their conditions are
    /// true, so a step that only runs in CI doesn't need its values locally.
    /// </remarks>
    private static void CheckRequirements(List<ResolvedStep> plan, HashSet<ResolvedStep> skipped, IReadOnlyList<BaunParameter> parameters)
    {
        var missing = new List<string>();

        foreach (ResolvedStep step in plan)
        {
            if (!skipped.Contains(step) && step.Builder.Conditions.Count == 0)
            {
                AddMissingRequirements(step, parameters, missing);
            }
        }

        if (missing.Count > 0)
        {
            throw new BuildConfigurationException(string.Join(Environment.NewLine, missing));
        }
    }

    /// <summary>
    /// Adds a message to <paramref name="missing"/> for each value <paramref name="step"/> requires that isn't set.
    /// </summary>
    /// <exception cref="BuildConfigurationException">A requirement throws.</exception>
    private static void AddMissingRequirements(ResolvedStep step, IReadOnlyList<BaunParameter> parameters, List<string> missing)
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

            if (value is not false && !ParameterResolver.IsMissing(value))
            {
                continue;
            }

            missing.Add(FindParameter(requirement, parameters) is { } parameter
                ? $"Step {step.Name} requires parameter {parameter.Name}. {ParameterResolver.DescribeSources(parameter)}"
                : $"Step {step.Name} requires {requirement.Body}, which {(value is false ? "is false" : "isn't set")}.");
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
    /// The command-line arguments, split into step names, steps to skip and parameter values.
    /// </summary>
    /// <param name="StepNames">The names of the steps to run.</param>
    /// <param name="SkipNames">The names of the steps given to <c>--skip</c>.</param>
    /// <param name="SkipAll">Whether <c>--skip</c> was given without a step, which skips all steps but the named ones.</param>
    /// <param name="ParameterValues">The values of the parameters.</param>
    private sealed record ParsedArguments(
        List<string> StepNames,
        List<string> SkipNames,
        bool SkipAll,
        Dictionary<BaunParameter, List<string>> ParameterValues);

    /// <summary>
    /// Splits the command-line arguments into step names, steps to skip and parameter values.
    /// </summary>
    private ParsedArguments ParseArguments(IReadOnlyList<string> arguments, IReadOnlyList<BaunParameter> parameters)
    {
        var names = new List<string>();
        var skipNames = new List<string>();
        bool skipAll = false;
        var values = new Dictionary<BaunParameter, List<string>>();
        var byName = parameters.ToDictionary(parameter => ParameterResolver.Normalize(parameter.Name));

        for (int i = 0; i < arguments.Count; i++)
        {
            string argument = arguments[i];

            if (IsOption(argument, StepOption))
            {
                // --step runs a step whose name is also a command of the baun tool, such as --step Init.
                if (!HasValueAfter(arguments, i))
                {
                    throw new BuildConfigurationException($"Option {argument} requires a step name.");
                }

                names.Add(arguments[++i]);
            }
            else if (IsOption(argument, SkipOption))
            {
                // --skip Test skips Test. Without a step, such as at the end, it skips all steps but the named ones.
                if (HasValueAfter(arguments, i))
                {
                    skipNames.Add(arguments[++i]);
                }
                else
                {
                    skipAll = true;
                }
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

        return new ParsedArguments(names, skipNames, skipAll, values);
    }

    private List<ResolvedStep> SelectRequestedSteps(List<ResolvedStep> steps, List<string> names)
    {
        if (names.Count == 0)
        {
            ResolvedStep step = steps.FirstOrDefault(step => step.IsEntry)
                                ?? throw new BuildConfigurationException(
                                    $"No step was named and {build.GetType().Name} has no entry step. Name a step, or add [Entry(nameof(...))] to the build class. {AvailableSteps(steps)}");

            return [step];
        }

        return [.. names.Select(name => FindStep(steps, name))];
    }

    /// <summary>
    /// Returns the steps of <paramref name="plan"/> that <c>--skip</c> skips: the named ones, or, when
    /// <paramref name="skipAll"/> is set, all but the <paramref name="requested"/> ones.
    /// </summary>
    private static HashSet<ResolvedStep> SelectSkippedSteps(
        List<ResolvedStep> steps,
        List<ResolvedStep> plan,
        List<ResolvedStep> requested,
        List<string> names,
        bool skipAll)
    {
        HashSet<ResolvedStep> skipped = [.. names.Select(name => FindStep(steps, name))];

        if (skipAll)
        {
            skipped.UnionWith(plan.Except(requested));
        }

        return skipped;
    }

    /// <summary>
    /// Returns the step named <paramref name="name"/>, ignoring case, or throws listing the available steps.
    /// </summary>
    private static ResolvedStep FindStep(List<ResolvedStep> steps, string name) =>
        steps.FirstOrDefault(step => string.Equals(step.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new BuildConfigurationException($"Unknown step {name}. {AvailableSteps(steps)}");

    private static string AvailableSteps(List<ResolvedStep> steps) =>
        $"Available steps: {string.Join(", ", steps.Select(step => step.Name))}.";

    /// <summary>
    /// Returns the steps to run for <paramref name="requested"/>, in the order they run.
    /// </summary>
    /// <remarks>
    /// The plan holds the requested steps, their dependencies and the steps they trigger, and again the dependencies and
    /// triggered steps of those. A step runs after its dependencies, and after the steps it runs after or is triggered by
    /// when they're in the plan. A triggered step runs as soon as it can after the step that triggers it.
    /// </remarks>
    private static List<ResolvedStep> CreateExecutionPlan(List<ResolvedStep> requested)
    {
        var included = new HashSet<ResolvedStep>();
        var pending = new Queue<ResolvedStep>(requested);

        while (pending.TryDequeue(out ResolvedStep? step))
        {
            if (included.Add(step))
            {
                foreach (ResolvedStep next in step.Dependencies.Concat(step.Triggered))
                {
                    pending.Enqueue(next);
                }
            }
        }

        var plan = new List<ResolvedStep>();
        var visited = new HashSet<ResolvedStep>();

        // The steps being visited, each with whether it was reached through a dependency rather than an order.
        var path = new List<(ResolvedStep Step, bool IsDependency)>();

        foreach (ResolvedStep step in requested)
        {
            Visit(step, isDependency: true);
        }

        return plan;

        void Visit(ResolvedStep step, bool isDependency)
        {
            if (visited.Contains(step))
            {
                return;
            }

            int index = path.FindIndex(item => item.Step == step);

            if (index >= 0)
            {
                var cycle = path.Skip(index).Select(item => item.Step.Name).Append(step.Name);
                bool onlyDependencies = isDependency && path.Skip(index + 1).All(item => item.IsDependency);

                throw new BuildConfigurationException(
                    $"{(onlyDependencies ? "Circular dependency" : "Circular order")}: {string.Join(" -> ", cycle)}.");
            }

            path.Add((step, isDependency));

            foreach (ResolvedStep dependency in step.Dependencies)
            {
                Visit(dependency, isDependency: true);
            }

            foreach (ResolvedStep earlier in step.OrderedAfter.Concat(step.TriggeredBy).Where(included.Contains))
            {
                Visit(earlier, isDependency: false);
            }

            path.RemoveAt(path.Count - 1);
            visited.Add(step);
            plan.Add(step);

            foreach (ResolvedStep triggered in step.Triggered)
            {
                Visit(triggered, isDependency: false);
            }
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

        /// <summary>
        /// Gets the steps this step runs after when they run too, from its <see cref="StepBuilder.After"/> and the
        /// <see cref="StepBuilder.Before"/> of other steps.
        /// </summary>
        public List<ResolvedStep> OrderedAfter { get; } = [];

        /// <summary>
        /// Gets the steps this step triggers, from its <see cref="StepBuilder.Triggers"/> and the
        /// <see cref="StepBuilder.TriggeredBy"/> of other steps.
        /// </summary>
        public List<ResolvedStep> Triggered { get; } = [];

        /// <summary>
        /// Gets the steps that trigger this step.
        /// </summary>
        public List<ResolvedStep> TriggeredBy { get; } = [];
    }

    private enum StepStatus
    {
        Succeeded,
        Failed,
        Skipped
    }

    private sealed record StepResult(string Name, StepStatus Status, TimeSpan? Duration);
}
