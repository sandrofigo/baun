namespace Baun;

/// <summary>
/// Runs code when a build is created. Implement it on the build class.
/// </summary>
public interface IOnBuildCreated
{
    /// <summary>
    /// Runs at the start of a run, before baun reads the arguments and sets the parameters.
    /// </summary>
    /// <remarks>If it throws, the build fails without running any step.</remarks>
    Task OnBuildCreatedAsync();
}

/// <summary>
/// Runs code when a build is initialized. Implement it on the build class.
/// </summary>
public interface IOnBuildInitialized
{
    /// <summary>
    /// Runs after baun sets the parameters and checks the requirements of the steps, right before the first step.
    /// It doesn't run for <c>--help</c>, <c>--plan</c> or a build that can't start, such as one with a missing parameter.
    /// </summary>
    /// <remarks>If it throws, the build fails without running any step.</remarks>
    Task OnBuildInitializedAsync();
}

/// <summary>
/// Runs code when a step starts. Implement it on the build class.
/// </summary>
public interface IOnStepStarted
{
    /// <summary>
    /// Runs before the work of each step.
    /// </summary>
    /// <remarks>If it throws, the step fails without doing its work.</remarks>
    /// <param name="step">The name of the step.</param>
    Task OnStepStartedAsync(string step);
}

/// <summary>
/// Runs code when a step succeeds. Implement it on the build class.
/// </summary>
public interface IOnStepSucceeded
{
    /// <summary>
    /// Runs after each step that succeeds.
    /// </summary>
    /// <remarks>If it throws, the step fails.</remarks>
    /// <param name="step">The name of the step.</param>
    Task OnStepSucceededAsync(string step);
}

/// <summary>
/// Runs code when a step fails. Implement it on the build class.
/// </summary>
public interface IOnStepFailed
{
    /// <summary>
    /// Runs after the step that fails.
    /// </summary>
    /// <param name="step">The name of the step.</param>
    /// <param name="exception">The exception the step threw.</param>
    Task OnStepFailedAsync(string step, Exception exception);
}

/// <summary>
/// Runs code when a step completes. Implement it on the build class.
/// </summary>
public interface IOnStepCompleted
{
    /// <summary>
    /// Runs after each step, whether it succeeds or fails.
    /// </summary>
    /// <param name="step">The name of the step.</param>
    Task OnStepCompletedAsync(string step);
}

/// <summary>
/// Runs code when a build succeeds. Implement it on the build class.
/// </summary>
public interface IOnBuildSucceeded
{
    /// <summary>
    /// Runs after the last step when every step succeeded, before the summary.
    /// </summary>
    /// <remarks>If it throws, the build fails.</remarks>
    Task OnBuildSucceededAsync();
}

/// <summary>
/// Runs code when a build fails. Implement it on the build class.
/// </summary>
public interface IOnBuildFailed
{
    /// <summary>
    /// Runs after a step fails, or after <see cref="IOnBuildInitialized"/> throws, before the summary.
    /// </summary>
    Task OnBuildFailedAsync();
}

/// <summary>
/// Runs code when a build completes. Implement it on the build class.
/// </summary>
public interface IOnBuildCompleted
{
    /// <summary>
    /// Runs after <see cref="IOnBuildSucceeded"/> or <see cref="IOnBuildFailed"/>, before the summary. Like them, it only
    /// runs when the build got as far as <see cref="IOnBuildInitialized"/>.
    /// </summary>
    Task OnBuildCompletedAsync();
}
