namespace Baun;

/// <summary>
/// A mistake in how a build is configured or run, such as an unknown step or an invalid parameter value. baun shows it
/// as a single error line instead of an exception.
/// </summary>
internal sealed class BuildConfigurationException(string message) : Exception(message);
