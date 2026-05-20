using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Hosting.Client;
using SteamInputBridge.Hosting.Server;
using SteamInputBridge.Settings;
using SteamInputBridge.Settings.Profiles;

namespace SteamInputBridge.App.Cli;

internal static class AppSetup
{
    public static IHost Create()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        // App-owned settings live under SteamInputBridge; top-level Logging is reserved for Microsoft logging.
        string settingsPath = Path.Combine(System.AppContext.BaseDirectory, "appsettings.json");
        _ = builder.Configuration.AddJsonFile(settingsPath, optional: true, reloadOnChange: true);

        // Register settings
        _ = builder.Services.AddApplicationSettings(builder.Configuration, settingsPath);
        _ = builder.Services.AddApplicationClient();
        _ = builder.Services.AddApplicationServer();
        _ = builder.Services.AddProfiles();

        // Configure settings
        SteamInputBridgeSettings settings = new();
        builder.Configuration.GetSection(SteamInputBridgeSettings.SectionName).Bind(settings);

        // Configure logging
        _ = builder.Logging.SetMinimumLevel(settings.Logging.Level);
        _ = builder.Logging.AddConsole();
        _ = builder.Logging.AddApplicationFileLogger(
            ResolveLogDirectory(settingsPath, settings.Logging.LogDirectory));

        return builder.Build();
    }

    private static string? ResolveLogDirectory(string settingsPath, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Path.IsPathFullyQualified(path))
        {
            return path;
        }

        string settingsDirectory = Path.GetDirectoryName(settingsPath) ?? System.AppContext.BaseDirectory;
        return Path.Combine(settingsDirectory, path);
    }
}
