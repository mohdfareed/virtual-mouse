using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SteamInputBridge.App.Tray;

internal static partial class WindowsThemeSupport
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    public static void ApplyToWindow(IntPtr window)
    {
        bool dark = IsDarkThemeEnabled();
        _ = SetPreferredAppMode(dark ? PreferredAppMode.AllowDark : PreferredAppMode.Default);
        FlushMenuThemes();

        int value = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(
            window,
            DwmwaUseImmersiveDarkMode,
            ref value,
            Marshal.SizeOf<int>());
    }

    private static bool IsDarkThemeEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int appsUseLightTheme && appsUseLightTheme == 0;
    }

    [DllImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SetPreferredAppMode(PreferredAppMode appMode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void FlushMenuThemes();

    private enum PreferredAppMode
    {
        Default,
        AllowDark,
    }
}
