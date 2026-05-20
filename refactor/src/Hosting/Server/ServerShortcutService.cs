using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Microsoft.Extensions.Logging;
using VirtualMouse.Forwarding;
using VirtualMouse.Settings;
using VirtualMouse.Shortcuts;

namespace VirtualMouse.Hosting;

internal sealed class ServerShortcutService(
    ApplicationSettingsService settings,
    IKeyboardShortcutListener listener,
    ControllerBroker controllers,
    MouseBroker mouse,
    ILogger<ServerShortcutService> logger) : IDisposable
{
    private readonly Lock _gate = new();
    private Dictionary<int, ShortcutEntry> _shortcuts = [];
    private bool _started;
    private bool _disposed;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _started = true;
        settings.Changed += OnSettingsChanged;
        Apply(settings.Current.Shortcuts);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        settings.Changed -= OnSettingsChanged;
        listener.Dispose();
        _disposed = true;
    }

    private void OnSettingsChanged(object? sender, ApplicationSettingsChangedEventArgs args)
    {
        _ = sender;
        Apply(args.Settings.Shortcuts);
    }

    private void Apply(Collection<ShortcutEntry> entries)
    {
        Dictionary<int, ShortcutEntry> shortcuts = [];
        List<KeyboardShortcutRegistration> registrations = [];
        HashSet<KeyboardShortcutCombination> combinations = [];
        for (int i = 0; i < entries.Count; i++)
        {
            ShortcutEntry entry = entries[i];
            KeyboardShortcutCombination combination;
            try
            {
                combination = KeyboardShortcutParser.Parse(entry.Keys);
            }
            catch (FormatException exception)
            {
                HostingLog.ShortcutSkipped(logger, ShortcutName(entry, i), exception.Message);
                continue;
            }

            if (!combinations.Add(combination))
            {
                HostingLog.ShortcutSkipped(logger, ShortcutName(entry, i), "duplicate key combination");
                continue;
            }

            int id = i + 1;
            shortcuts[id] = entry;
            registrations.Add(new KeyboardShortcutRegistration(id, combination));
        }

        lock (_gate)
        {
            _shortcuts = shortcuts;
        }

        try
        {
            listener.Update(registrations, OnShortcutPressed);
            HostingLog.ShortcutsRegistered(logger, registrations.Count);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            HostingLog.ShortcutRegistrationFailed(logger, exception.Message);
        }
    }

    private void OnShortcutPressed(int id)
    {
        ShortcutEntry? shortcut;
        lock (_gate)
        {
            _ = _shortcuts.TryGetValue(id, out shortcut);
        }

        if (shortcut is null || !shortcut.Target.HasValue || !shortcut.Value.HasValue)
        {
            return;
        }

        bool enabled = shortcut.Value.Value == ShortcutValue.Enabled;
        switch (shortcut.Target.Value)
        {
            case ShortcutTarget.Motion:
                controllers.SetPhysicalMotionEnabled(enabled);
                break;
            case ShortcutTarget.Pointer:
                mouse.SetPointerOutputEnabled(enabled);
                break;
            default:
                return;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            string name = ShortcutName(shortcut, id - 1);
            HostingLog.ShortcutApplied(logger, name, shortcut.Target.Value, shortcut.Value.Value);
        }
    }

    private static string ShortcutName(ShortcutEntry entry, int index)
    {
        return string.IsNullOrWhiteSpace(entry.Name)
            ? $"#{index}"
            : entry.Name;
    }
}
