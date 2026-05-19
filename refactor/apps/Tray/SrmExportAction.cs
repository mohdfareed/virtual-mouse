using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VirtualMouse.Settings;
using VirtualMouse.Settings.Profiles;
using VirtualMouse.Steam;

namespace VirtualMouse.Tray;

internal static class SrmExportAction
{
    public static void Export(IServiceProvider services)
    {
        try
        {
            ProfilesService profiles = services.GetRequiredService<ProfilesService>();
            ApplicationSettingsService settings =
                services.GetRequiredService<ApplicationSettingsService>();
            SettingsFile settingsFile = services.GetRequiredService<SettingsFile>();
            string manifestPath = ResolveManifestPath(
                settings.Current.Steam.SrmExportPath,
                settingsFile.Path);
            string shortcutPath = Path.Combine(
                System.AppContext.BaseDirectory,
                "Shortcut.exe");
            string manifest = SteamRomManagerExport.CreateJson(profiles, shortcutPath);

            File.WriteAllText(manifestPath, manifest);
            _ = MessageBox.Show(
                $"Exported {profiles.ListProfileIds().Count} profiles to:\n{manifestPath}",
                "Virtual Mouse",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _ = MessageBox.Show(
                exception.Message,
                "Virtual Mouse",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string ResolveManifestPath(string? path, string settingsPath)
    {
        string filePath = Environment.ExpandEnvironmentVariables(
            string.IsNullOrWhiteSpace(path) ? "srm-manifest.json" : path);
        return Path.IsPathFullyQualified(filePath)
            ? filePath
            : Path.Combine(
                Path.GetDirectoryName(settingsPath) ?? System.AppContext.BaseDirectory,
                filePath);
    }
}
