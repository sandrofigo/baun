using BaunCli;

using System.Diagnostics;

// Finds the build in the current folder or its "build" subfolder and runs it with the .NET SDK,
// passing all arguments through, for example "baun Test".

var build = BuildLocator.Find(Directory.GetCurrentDirectory());

if (build is null)
{
    Console.Error.WriteLine("error: No baun build found. Expected a single-file Build.cs, or a Build.cs next to a project file, in the current folder or its 'build' subfolder.");
    return 1;
}

var startInfo = new ProcessStartInfo("dotnet");

startInfo.ArgumentList.Add("run");

if (build.ProjectFile is not null)
{
    startInfo.ArgumentList.Add("--project");
    startInfo.ArgumentList.Add(build.ProjectFile);
}
else
{
    startInfo.ArgumentList.Add("--file");
    startInfo.ArgumentList.Add(build.BuildFile);
}

startInfo.ArgumentList.Add("--");

foreach (var argument in args)
{
    startInfo.ArgumentList.Add(argument);
}

using var process = Process.Start(startInfo)
    ?? throw new InvalidOperationException("Could not start 'dotnet'.");

await process.WaitForExitAsync();

return process.ExitCode;
