using System.Reflection;

namespace Baun;

/// <summary>
/// Reads the version of baun from an assembly.
/// </summary>
internal static class BaunVersion
{
    /// <summary>
    /// Gets the informational version of <paramref name="assembly"/> without build metadata.
    /// </summary>
    public static string Get(Assembly assembly)
    {
        string version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        // Drop build metadata such as "+3a69508".
        int metadata = version.IndexOf('+');
        return metadata >= 0 ? version[..metadata] : version;
    }
}
