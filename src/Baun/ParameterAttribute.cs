namespace Baun;

/// <summary>
/// Marks a field or property of the build class as a parameter, which baun sets before it runs any step.
/// </summary>
/// <remarks>
/// A parameter is set from, in order of precedence:
/// <list type="number">
/// <item>the command line, such as <c>--configuration Release</c></item>
/// <item>an environment variable, such as <c>CONFIGURATION=Release</c></item>
/// <item><c>.baun/parameters.json</c> in the nearest <c>.baun</c> folder of the current directory or its parents, such as <c>{ "Configuration": "Release" }</c></item>
/// </list>
/// When none of them sets it, the parameter keeps its initial value. Names match case-insensitively and ignore
/// <c>-</c> and <c>_</c>, so <c>NuGetApiKey</c> is set by <c>--nuget-api-key</c> and <c>NUGET_API_KEY</c>.
/// <code>
/// partial class Build
/// {
///     [Parameter("Configuration to build")]
///     string Configuration = "Debug";
/// }
/// </code>
/// baun reports a compiler error for a parameter it can't set, such as a <c>readonly</c> field or an unsupported type.
/// </remarks>
/// <param name="description">The description shown in the build's help.</param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class ParameterAttribute(string? description = null) : Attribute
{
    /// <summary>
    /// Gets the description shown in the build's help.
    /// </summary>
    public string? Description { get; } = description;

    /// <summary>
    /// Gets or sets the parameter's name, if it differs from the name of the field or property.
    /// </summary>
    public string? Name { get; init; }
}
