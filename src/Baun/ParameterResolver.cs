using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Baun.DotNet;

namespace Baun;

/// <summary>
/// Where parameter values come from, besides the command line.
/// </summary>
/// <param name="Environment">The environment variables.</param>
/// <param name="ParametersFile">The path of <c>.baun/parameters.json</c>, or <see langword="null"/> for none.</param>
internal sealed record ParameterSources(IReadOnlyDictionary<string, string> Environment, AbsolutePath? ParametersFile)
{
    /// <summary>
    /// No environment variables and no parameters file.
    /// </summary>
    public static ParameterSources None { get; } = new(new Dictionary<string, string>(), null);

    /// <summary>
    /// The environment variables of this process, and <c>.baun/parameters.json</c> in the nearest <c>.baun</c> folder
    /// in the current directory or its parents.
    /// </summary>
    public static ParameterSources Default() => ForDirectory(AbsolutePath.CurrentDirectory);

    /// <summary>
    /// The environment variables of this process, and <c>.baun/parameters.json</c> in the nearest <c>.baun</c> folder
    /// in <paramref name="directory"/> or its parents. Without a <c>.baun</c> folder, there is no parameters file.
    /// </summary>
    public static ParameterSources ForDirectory(AbsolutePath directory)
    {
        var environment = new Dictionary<string, string>();

        foreach (DictionaryEntry variable in System.Environment.GetEnvironmentVariables())
        {
            environment[(string)variable.Key] = (string?)variable.Value ?? string.Empty;
        }

        return new ParameterSources(environment, RepositoryRoot.TryGetParametersFile(directory));
    }
}

/// <summary>
/// Sets the parameters of a build from the command line, environment variables and <c>.baun/parameters.json</c>.
/// </summary>
internal static class ParameterResolver
{
    /// <summary>
    /// How the parameters file is named in messages.
    /// </summary>
    public const string ParametersFileDisplayName = RepositoryRoot.FolderName + "/" + RepositoryRoot.ParametersFileName;

    /// <summary>
    /// Returns <paramref name="name"/> without <c>-</c> and <c>_</c>, in upper case, so names that differ only in case
    /// and separators match, such as <c>NuGetApiKey</c>, <c>nuget-api-key</c> and <c>NUGET_API_KEY</c>.
    /// </summary>
    public static string Normalize(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (char c in name)
        {
            if (c is not '-' and not '_')
            {
                builder.Append(char.ToUpperInvariant(c));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns the command-line option of a parameter, such as <c>--nu-get-api-key</c> for <c>NuGetApiKey</c>.
    /// </summary>
    public static string ToOption(string name)
    {
        var builder = new StringBuilder("--");

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];

            if (c == '_')
            {
                c = '-';
            }

            bool startsWord = char.IsUpper(c) && i > 0 && builder[^1] != '-' &&
                              (!char.IsUpper(name[i - 1]) || i + 1 < name.Length && char.IsLower(name[i + 1]));

            if (startsWord)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns the environment variable of a parameter, such as <c>NU_GET_API_KEY</c> for <c>NuGetApiKey</c>.
    /// </summary>
    public static string ToEnvironmentVariable(string name) =>
        ToOption(name)[2..].Replace('-', '_').ToUpperInvariant();

    /// <summary>
    /// Returns how to set <paramref name="parameter"/>, such as
    /// <c>Pass --solution, set SOLUTION or add it to .baun/parameters.json.</c>
    /// </summary>
    public static string DescribeSources(BaunParameter parameter) =>
        $"Pass {ToOption(parameter.Name)}, set {ToEnvironmentVariable(parameter.Name)} or add it to {ParametersFileDisplayName}.";

    /// <summary>
    /// Returns whether <paramref name="value"/> counts as not set for a required parameter: <see langword="null"/>, an
    /// empty string or an empty array.
    /// </summary>
    public static bool IsMissing(object? value) => value is null or string { Length: 0 } or Array { Length: 0 };

    /// <summary>
    /// Returns whether <paramref name="parameter"/> takes several values.
    /// </summary>
    public static bool IsArray(BaunParameter parameter) => parameter.Type.IsArray;

    /// <summary>
    /// Returns whether <paramref name="parameter"/> is a single <see cref="bool"/>, which needs no value on the command line.
    /// </summary>
    public static bool IsFlag(BaunParameter parameter) =>
        (Nullable.GetUnderlyingType(parameter.Type) ?? parameter.Type) == typeof(bool);

    /// <summary>
    /// Sets each parameter from the source with the highest precedence that has a value for it: the command line, then
    /// environment variables, then the parameters file. Parameters without a value keep their initial value.
    /// </summary>
    /// <param name="parameters">The parameters of the build.</param>
    /// <param name="commandLine">The values given on the command line, by parameter.</param>
    /// <param name="sources">The environment variables and the parameters file.</param>
    /// <exception cref="BuildConfigurationException">
    /// A value is invalid, the parameters file is, or a required parameter has no value.
    /// </exception>
    public static void Apply(
        IReadOnlyList<BaunParameter> parameters,
        IReadOnlyDictionary<BaunParameter, List<string>> commandLine,
        ParameterSources sources)
    {
        if (parameters.Count == 0 && sources.ParametersFile is null)
        {
            return;
        }

        var values = SetValues(parameters, commandLine, sources);

        var missing = parameters
            .Where(parameter => parameter.IsRequired && (!values.TryGetValue(parameter, out object? value) || IsMissing(value)))
            .Select(parameter => $"Parameter {parameter.Name} is required. {DescribeSources(parameter)}")
            .ToList();

        if (missing.Count > 0)
        {
            throw new BuildConfigurationException(string.Join(Environment.NewLine, missing));
        }
    }

    /// <summary>
    /// Sets the parameters that have a value, and returns the value each of them got.
    /// </summary>
    private static Dictionary<BaunParameter, object?> SetValues(
        IReadOnlyList<BaunParameter> parameters,
        IReadOnlyDictionary<BaunParameter, List<string>> commandLine,
        ParameterSources sources)
    {
        var values = new Dictionary<BaunParameter, object?>();
        var byName = parameters.ToDictionary(parameter => Normalize(parameter.Name));
        var fileValues = ReadParametersFile(sources.ParametersFile, byName);

        var environment = new Dictionary<string, (string Name, string Value)>();

        foreach ((string name, string value) in sources.Environment.OrderBy(variable => variable.Key, StringComparer.Ordinal))
        {
            environment.TryAdd(Normalize(name), (name, value));
        }

        foreach (BaunParameter parameter in parameters)
        {
            string key = Normalize(parameter.Name);

            if (commandLine.TryGetValue(parameter, out List<string>? arguments))
            {
                values[parameter] = Set(parameter, arguments, $"option {ToOption(parameter.Name)}");
            }
            else if (environment.TryGetValue(key, out var variable) && variable.Value.Length > 0)
            {
                // An empty variable counts as not set: CI servers often pass secrets that aren't defined as empty values.
                List<string> items = IsArray(parameter)
                    ? [.. variable.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)]
                    : [variable.Value];

                values[parameter] = Set(parameter, items, $"environment variable {variable.Name}");
            }
            else if (fileValues.TryGetValue(parameter, out List<string>? items))
            {
                values[parameter] = Set(parameter, items, ParametersFileDisplayName);
            }
        }

        return values;
    }

    /// <summary>
    /// Reads the values of <paramref name="path"/>, by parameter. A missing file has no values.
    /// </summary>
    private static Dictionary<BaunParameter, List<string>> ReadParametersFile(AbsolutePath? path, Dictionary<string, BaunParameter> byName)
    {
        var values = new Dictionary<BaunParameter, List<string>>();

        if (path is null || !File.Exists(path))
        {
            return values;
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(
                File.ReadAllText(path),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException exception)
        {
            throw new BuildConfigurationException($"{ParametersFileDisplayName} is not valid JSON: {exception.Message}");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new BuildConfigurationException($"{ParametersFileDisplayName} must contain a JSON object, such as {{ \"Configuration\": \"Release\" }}.");
            }

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!byName.TryGetValue(Normalize(property.Name), out BaunParameter? parameter))
                {
                    string available = byName.Count == 0
                        ? "The build has no parameters."
                        : $"Available parameters: {string.Join(", ", byName.Values.Select(item => item.Name))}.";

                    throw new BuildConfigurationException($"Unknown parameter {property.Name} in {ParametersFileDisplayName}. {available}");
                }

                JsonElement value = property.Value;

                if (value.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.Array)
                {
                    if (!IsArray(parameter))
                    {
                        throw new BuildConfigurationException($"Parameter {parameter.Name} in {ParametersFileDisplayName} takes a single value, not an array.");
                    }

                    values[parameter] = [.. value.EnumerateArray().Select(item => ReadScalar(item, parameter))];
                }
                else
                {
                    values[parameter] = [ReadScalar(value, parameter)];
                }
            }
        }

        return values;
    }

    private static string ReadScalar(JsonElement value, BaunParameter parameter) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => throw new BuildConfigurationException(
            $"Parameter {parameter.Name} in {ParametersFileDisplayName} must be a string, number or boolean{(IsArray(parameter) ? ", or an array of them" : string.Empty)}."),
    };

    /// <summary>
    /// Converts <paramref name="values"/> to the type of <paramref name="parameter"/>, sets it and returns the value.
    /// </summary>
    private static object? Set(BaunParameter parameter, List<string> values, string source)
    {
        if (!IsArray(parameter))
        {
            object? value = Convert(parameter, parameter.Type, values.Single(), source, item: null);
            parameter.Set(value);
            return value;
        }

        Type elementType = parameter.Type.GetElementType()!;
        var array = Array.CreateInstance(elementType, values.Count);

        for (int i = 0; i < values.Count; i++)
        {
            array.SetValue(Convert(parameter, elementType, values[i], source, item: i + 1), i);
        }

        parameter.Set(array);
        return array;
    }

    private static object? Convert(BaunParameter parameter, Type type, string value, string source, int? item)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;

        try
        {
            if (underlying == typeof(AbsolutePath))
            {
                return ToAbsolutePath(value, item is not null);
            }

            if (underlying == typeof(Solution))
            {
                return ToAbsolutePath(value, item is not null) is { } path ? Solution.Read(path) : null;
            }

            if (underlying == typeof(Project))
            {
                return ToAbsolutePath(value, item is not null) is { } path ? Project.Read(path) : null;
            }

            return TypeDescriptor.GetConverter(type).ConvertFromString(null, CultureInfo.InvariantCulture, value);
        }
        catch (Exception exception) when (exception is not BuildConfigurationException)
        {
            string position = item is null ? string.Empty : $"item {item} ";
            string validValues = underlying.IsEnum ? $" Valid values: {string.Join(", ", Enum.GetNames(underlying))}." : string.Empty;

            // A missing or invalid solution or project file says why.
            string reason = underlying == typeof(Solution) || underlying == typeof(Project) ? $" {exception.Message}" : string.Empty;

            throw new BuildConfigurationException(
                $"Parameter {parameter.Name}: {position}\"{value}\" from {source} is not a valid {underlying.Name}.{validValues}{reason}");
        }
    }

    /// <summary>
    /// Converts <paramref name="value"/> to an absolute path. A relative path is relative to
    /// <see cref="BaunBuild.RootDirectory"/>, so it doesn't depend on the folder the build runs from. An empty value
    /// is <see langword="null"/>, not the root directory, so an empty <c>--output</c> can't make a build clean the
    /// whole repository. An array item can't be <see langword="null"/>, so an empty one throws.
    /// </summary>
    private static AbsolutePath? ToAbsolutePath(string value, bool isArrayItem)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return isArrayItem ? throw new FormatException("An array item can't be an empty path.") : null;
        }

        return Path.IsPathFullyQualified(value) ? (AbsolutePath)value : BaunBuild.RootDirectory / value;
    }
}
