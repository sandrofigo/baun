using static Baun.EnvironmentVariables;

namespace Baun.CI.TeamCity;

/// <summary>
/// Information about the TeamCity build the build runs in, read from the
/// <see href="https://www.jetbrains.com/help/teamcity/predefined-build-parameters.html">predefined build parameters</see> that TeamCity sets as environment variables.
/// </summary>
/// <remarks>
/// Each property reads its environment variable when it's used, so setting the variable yourself works too.
/// </remarks>
public static class TeamCityBuild
{
    /// <summary>
    /// Gets whether the build runs in TeamCity: <c>TEAMCITY_VERSION</c> is set.
    /// </summary>
    public static bool IsRunning => Get("TEAMCITY_VERSION") is not null;
}
