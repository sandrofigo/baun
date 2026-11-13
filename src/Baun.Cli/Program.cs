using Baun.Cli;

return await CliApp.RunAsync(args, Directory.GetCurrentDirectory(), CliConsole.Create(), ProcessRunner.RunAsync);
