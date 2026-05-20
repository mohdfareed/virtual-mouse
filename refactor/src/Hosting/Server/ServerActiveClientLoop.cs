using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VirtualMouse.Forwarding;
using VirtualMouse.HidHide;
using VirtualMouse.Runtime;
using VirtualMouse.Settings.Profiles;
using VirtualMouse.Steam;
using ProfileControllerOutput = VirtualMouse.Settings.Profiles.ControllerOutput;

namespace VirtualMouse.Hosting;

internal sealed class ServerActiveClientLoop(
    ActiveClientRegistry clients,
    Func<int> getForegroundProcessId,
    TimeSpan pollInterval,
    Action<ActiveClientChangedEventArgs>? activeClientChanged,
    ILogger? logger = null,
    SteamInputClient? steam = null,
    ProfilesService? profiles = null,
    HidHideProfileFirewall? hidHide = null,
    Func<ActiveClientRegistryStatus, Guid, IReadOnlyList<string>>? getHidHideDevices = null,
    ControllerBroker? forwarding = null,
    MouseBroker? mouseForwarding = null)
{
    private readonly Lock _steamStatusGate = new();
    private ServerSteamInputStatus _steamStatus = new(false, null, null, null);

    private static readonly TimeSpan ForegroundPollDelay = TimeSpan.FromMilliseconds(100);

    public static ServerActiveClientLoop CreateDefault(
        ActiveClientRegistry clients,
        ILogger logger,
        ProfilesService? profiles = null,
        HidHideProfileFirewall? hidHide = null,
        Func<ActiveClientRegistryStatus, Guid, IReadOnlyList<string>>? getHidHideDevices = null,
        ControllerBroker? forwarding = null,
        MouseBroker? mouseForwarding = null)
    {
        return new ServerActiveClientLoop(
            clients,
            GetForegroundProcessId,
            ForegroundPollDelay,
            activeClientChanged: null,
            logger,
            new SteamInputClient(),
            profiles,
            hidHide,
            getHidHideDevices,
            forwarding,
            mouseForwarding);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        clients.ActiveClientChanged += OnActiveClientChanged;
        try
        {
            int lastForegroundProcessId = -1;
            while (!cancellationToken.IsCancellationRequested)
            {
                int foregroundProcessId = getForegroundProcessId();
                if (foregroundProcessId != lastForegroundProcessId)
                {
                    clients.RefreshClients(foregroundProcessId);
                    UpdateHidHide(clients.GetStatus().ActiveClientId);
                    lastForegroundProcessId = foregroundProcessId;
                }

                await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            clients.ActiveClientChanged -= OnActiveClientChanged;
            hidHide?.Clear();
        }
    }

    public ServerSteamInputStatus GetSteamInputStatus()
    {
        lock (_steamStatusGate)
        {
            return _steamStatus;
        }
    }

    public void RefreshHidHide()
    {
        UpdateHidHide(clients.GetStatus().ActiveClientId);
    }

    private void OnActiveClientChanged(object? sender, ActiveClientChangedEventArgs args)
    {
        if (activeClientChanged is not null)
        {
            activeClientChanged(args);
            return;
        }

        ActiveClientChanged(args);
    }

    private static int GetForegroundProcessId()
    {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return 0;
        }

        _ = GetWindowThreadProcessId(window, out uint processId);
        return processId <= int.MaxValue ? (int)processId : 0;
    }

    private void ActiveClientChanged(ActiveClientChangedEventArgs args)
    {
        if (logger is null || steam is null)
        {
            return;
        }

        HostingLog.ActiveClientChanged(logger, args.PreviousClientId, args.CurrentClientId);

        forwarding?.SetActiveClient(args.CurrentClientId);
        mouseForwarding?.SetActiveClient(args.CurrentClientId);

        try
        {
            uint? appId = FindSteamAppId(clients.GetStatus(), args.CurrentClientId);
            HostingLog.ClearingForcedSteamInputAppId(logger);
            steam.ForceConfigAsync(null).AsTask().GetAwaiter().GetResult();

            if (appId.HasValue)
            {
                HostingLog.ForcingSteamInputAppId(logger, appId.Value, args.CurrentClientId);
                steam.ForceConfigAsync(appId.Value).AsTask().GetAwaiter().GetResult();
                SetSteamInputStatus(new ServerSteamInputStatus(true, appId.Value, args.CurrentClientId, null));
            }
            else
            {
                HostingLog.NoSteamInputAppIdToForce(logger);
                SetSteamInputStatus(new ServerSteamInputStatus(false, null, args.CurrentClientId, null));
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                ArgumentException or
                System.ComponentModel.Win32Exception)
        {
            HostingLog.SteamInputForcingFailed(logger, args.CurrentClientId, exception.Message);
            SetSteamInputStatus(new ServerSteamInputStatus(false, null, args.CurrentClientId, exception.Message));
        }

        UpdateHidHide(args.CurrentClientId);
    }

    private void UpdateHidHide(Guid? clientId)
    {
        if (logger is null || hidHide is null)
        {
            return;
        }

        try
        {
            if (!clientId.HasValue || profiles is null)
            {
                hidHide.Clear();
                return;
            }

            ActiveClientRegistryStatus status = clients.GetStatus();
            ClientStatus? client = FindClient(status, clientId.Value);
            if (client is null)
            {
                hidHide.Clear();
                return;
            }

            GameProfile? profile = profiles.GetProfile(client.ProfileId);
            if (profile is null ||
                profile.ControllerOutput.GetValueOrDefault(ProfileControllerOutput.None) == ProfileControllerOutput.None ||
                forwarding?.GetStatus().ControllerOutputEnabled == false)
            {
                hidHide.Clear();
                return;
            }

            HidHideScope scope = HidHideScope.Create(
                getHidHideDevices?.Invoke(status, clientId.Value) ?? [],
                GetExecutablePaths(client.OwnedProcesses));
            hidHide.Apply(scope);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                ArgumentException or
                System.ComponentModel.Win32Exception or
                System.IO.IOException or
                UnauthorizedAccessException)
        {
            HostingLog.HidHideUpdateFailed(logger, clientId, exception.Message);
        }
    }

    private void SetSteamInputStatus(ServerSteamInputStatus status)
    {
        lock (_steamStatusGate)
        {
            _steamStatus = status;
        }
    }

    private static uint? FindSteamAppId(
        ActiveClientRegistryStatus status,
        Guid? clientId)
    {
        if (!clientId.HasValue)
        {
            return null;
        }

        foreach (ClientStatus client in status.Clients)
        {
            if (client.ClientId == clientId)
            {
                return client.SteamAppId;
            }
        }

        return null;
    }

    private static ClientStatus? FindClient(ActiveClientRegistryStatus status, Guid clientId)
    {
        foreach (ClientStatus client in status.Clients)
        {
            if (client.ClientId == clientId)
            {
                return client;
            }
        }

        return null;
    }

    private static List<string> GetExecutablePaths(IReadOnlyList<ObservedGameProcess> processes)
    {
        List<string> paths = [];
        foreach (ObservedGameProcess process in processes)
        {
            if (GameProcessHost.GetExecutablePath(process.ProcessId) is { Length: > 0 } path)
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
