using System.Diagnostics;
using Baun.CI;
using Baun.Logging;
using static Baun.EnvironmentVariables;

namespace Baun;

/// <summary>
/// Opens files in the default browser of the user.
/// </summary>
internal static class Browser
{
    /// <summary>
    /// Opens the file at <paramref name="path"/> in the default browser.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when no browser can be shown, such as on a CI server, in an SSH session, without a display,
    /// with redirected output, or when starting the browser fails.
    /// </returns>
    public static bool TryOpen(string path)
    {
        if (GetReasonToNotOpen() is { } reason)
        {
            Log.Debug("Not opening the browser: {Reason}", reason);
            return false;
        }

        ProcessStartInfo startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo(path) { UseShellExecute = true }
            : new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { ArgumentList = { path }, UseShellExecute = false };

        try
        {
            using Process? process = Process.Start(startInfo);
            return process is not null || OperatingSystem.IsWindows();
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "Could not open the browser");
            return false;
        }
    }

    private static string? GetReasonToNotOpen()
    {
        if (CIProviders.Detect() != CIProvider.None || IsTrue("CI"))
        {
            return "running on a CI server";
        }

        if (Console.IsOutputRedirected)
        {
            return "the output is redirected";
        }

        if (Get("SSH_CONNECTION") is not null || Get("SSH_TTY") is not null)
        {
            return "running in an SSH session";
        }

        if (OperatingSystem.IsLinux() && Get("DISPLAY") is null && Get("WAYLAND_DISPLAY") is null)
        {
            return "there is no display";
        }

        return null;
    }
}
