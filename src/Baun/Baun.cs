/// <summary>
/// Runs baun builds.
/// </summary>
public static class Baun
{
    /// <summary>
    /// Runs the steps named on the command line, or the step marked with <see cref="EntryAttribute"/> if none are named.
    /// </summary>
    /// <remarks>
    /// The command-line arguments are read from <see cref="Environment.GetCommandLineArgs"/>.
    /// If the build fails, <see cref="Environment.ExitCode"/> is set to a non-zero value.
    /// </remarks>
    /// <typeparam name="TBuild">The build class. It must be <c>partial</c> so baun can complete it.</typeparam>
    public static async Task RunAsync<TBuild>()
        where TBuild : IBaunBuild, new()
    {
        var build = new TBuild();
        var arguments = Environment.GetCommandLineArgs()[1..];

        var exitCode = await new BuildRunner(build, Console.Out, Console.Error).RunAsync(arguments);

        if (exitCode != 0)
        {
            Environment.ExitCode = exitCode;
        }
    }
}
