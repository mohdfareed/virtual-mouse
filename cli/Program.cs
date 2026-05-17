using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Profiles;

internal static class Program
{
    // MARK: Entry
    // ========================================================================

    private static async Task<int> Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        _ = builder.Configuration.AddJsonFile(
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            optional: true,
            reloadOnChange: true);
        _ = builder.Logging.ClearProviders();
        _ = builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });
        _ = builder.Services.Configure<CliViiperSettings>(
            builder.Configuration.GetSection(CliViiperSettings.SectionName));
        _ = builder.Services.Configure<SteamRomManagerSettings>(
            builder.Configuration.GetSection("SteamRomManager"));
        IReadOnlyDictionary<string, GameProfile> profiles =
            builder.Configuration.GetSection("Profiles").Get<Dictionary<string, GameProfile>>() ??
            new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);
        _ = builder.Services.AddSingleton(profiles);

        using IHost host = builder.Build();
        IServiceProvider services = host.Services;
        RootCommand root = new("Local input forwarding CLI.");

        if (OperatingSystem.IsWindows())
        {
            root.Subcommands.Add(HostCommands.CreateHostCommand(services));
            root.Subcommands.Add(ClientCommands.CreateClientCommand());
            root.Subcommands.Add(SteamCommands.CreateSteamCommand(services));
            root.Subcommands.Add(TestCommands.CreateTestCommand(services));
        }

        return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
    }
}
