using Tunnela.Contracts.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Tunnela.Contracts;
using Tunnela.Service;

// The signal helper is an internal, SYSTEM-only operation, never an IPC command.
if (args.Length == 2 && args[0] == "--signal-engine")
{
    return EngineSignalHelper.Run(args[1]);
}

// Running from a terminal must never accidentally change the host network.
if (!OperatingSystem.IsWindows() || !WindowsServiceHelpers.IsWindowsService())
{
    Console.Error.WriteLine(Messages.GetEnglish("Service.ScmRequired"));
    return 1;
}

try
{
    var installation = InstallConfiguration.LoadAndValidate();
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders(); // Never send raw engine output or imported configuration to Event Log.
    builder.Services.AddWindowsService(options => options.ServiceName = ServiceProtocol.ServiceName);
    builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(25));
    builder.Services.AddSingleton(installation);
    builder.Services.AddSingleton<EngineSupervisor>();
    builder.Services.AddHostedService<ControlPipeWorker>();
    await builder.Build().RunAsync();
    return 0;
}
catch
{
    // Do not put paths, configuration, exception messages, or credentials into startup diagnostics.
    Console.Error.WriteLine(Messages.GetEnglish("Service.StartupFailed"));
    return 2;
}
