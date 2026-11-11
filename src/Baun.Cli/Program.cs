using BaunCli;

return await CliApp.RunAsync(
    args,
    Directory.GetCurrentDirectory(),
    new CliConsole(Console.In, Console.Out, Console.Error, IsInteractive: !Console.IsInputRedirected),
    ProcessRunner.RunAsync);
