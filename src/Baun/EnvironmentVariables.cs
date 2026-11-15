using System.Globalization;

namespace Baun;

/// <summary>
/// Reads environment variables. An empty variable counts as not set, because CI servers often pass undefined values as
/// empty ones.
/// </summary>
internal static class EnvironmentVariables
{
    /// <summary>
    /// Returns the value of the variable <paramref name="name"/>, or <see langword="null"/> if it's not set or empty.
    /// </summary>
    public static string? Get(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    /// <summary>
    /// Returns whether the variable <paramref name="name"/> is <c>true</c>, ignoring case.
    /// </summary>
    public static bool IsTrue(string name) => string.Equals(Get(name), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the variable <paramref name="name"/> as a non-negative whole number, or <see langword="null"/> if it's not
    /// set or not a number.
    /// </summary>
    public static long? GetNumber(string name) => long.TryParse(Get(name), NumberStyles.None, CultureInfo.InvariantCulture, out long number) ? number : null;
}
