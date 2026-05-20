using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Forwarding.Mouse;
using SteamInputBridge.Settings;
using SteamInputBridge.Shortcuts;

namespace SteamInputBridge.Hosting.Server;

internal sealed class ServerShortcutService(
    ApplicationSettingsService settings,
    IKeyboardShortcutListener listener,
    ControllerBroker controllers,
    MouseBroker mouse,
    ILogger<ServerShortcutService> logger) : IDisposable
{
    private readonly Lock _gate = new();
    private Dictionary<int, IReadOnlyList<ShortcutEntry>> _shortcuts = [];
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
        Dictionary<int, List<ShortcutEntry>> shortcuts = [];
        List<KeyboardShortcutRegistration> registrations = [];
        Dictionary<KeyboardShortcutCombination, int> idsByCombination = [];
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

            if (!idsByCombination.TryGetValue(combination, out int id))
            {
                id = idsByCombination.Count + 1;
                idsByCombination[combination] = id;
                shortcuts[id] = [];
                registrations.Add(new KeyboardShortcutRegistration(id, combination));
            }

            shortcuts[id].Add(entry);
        }

        lock (_gate)
        {
            _shortcuts = shortcuts.ToDictionary(
                static item => item.Key,
                static item => (IReadOnlyList<ShortcutEntry>)item.Value);
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
        IReadOnlyList<ShortcutEntry>? shortcuts;
        lock (_gate)
        {
            _ = _shortcuts.TryGetValue(id, out shortcuts);
        }

        if (shortcuts is null)
        {
            return;
        }

        foreach (ShortcutEntry shortcut in shortcuts)
        {
            if (!shortcut.Target.HasValue || !shortcut.Value.HasValue)
            {
                continue;
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
                    continue;
            }

            if (logger.IsEnabled(LogLevel.Information))
            {
                string name = ShortcutName(shortcut, id - 1);
                HostingLog.ShortcutApplied(logger, name, shortcut.Target.Value, shortcut.Value.Value);
            }
        }
    }

    private static string ShortcutName(ShortcutEntry entry, int index)
    {
        return string.IsNullOrWhiteSpace(entry.Name)
            ? $"#{index}"
            : entry.Name;
    }
}
