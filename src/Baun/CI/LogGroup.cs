namespace Baun.CI;

/// <summary>
/// Groups log output into collapsible sections on CI servers.
/// </summary>
/// <remarks>
/// <para>
/// On GitHub Actions, Azure Pipelines, GitLab CI/CD and TeamCity, the output written while a group is open can be
/// collapsed in the log of the run. baun already puts each step in a group, named after the step.
/// </para>
/// <para>
/// Groups don't nest: a group opened inside another one, such as inside a step, is written as a plain line with its
/// name. Outside CI, a group is also written as a plain line.
/// </para>
/// <code>
/// using (LogGroup.Begin("Restore"))
/// {
///     // output of the restore
/// }
/// </code>
/// </remarks>
public static class LogGroup
{
    /// <summary>
    /// Opens a log group, which ends when the returned object is disposed.
    /// </summary>
    /// <param name="name">The name of the group, shown in the log.</param>
    /// <returns>An object that ends the group when it's disposed.</returns>
    public static IDisposable Begin(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var console = BaunConsole.Create();
        return LogGroups.Begin(console, CIProviders.Detect(), name, Theme.Faint($"{console.Chevron()} {name}"));
    }
}
