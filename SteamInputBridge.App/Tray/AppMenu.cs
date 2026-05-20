using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using SteamInputBridge.Forwarding.Controller;
using SteamInputBridge.Hosting.Server;
using SteamInputBridge.Runtime;

namespace SteamInputBridge.App.Tray;

internal sealed class AppMenu(
    string settingsPath,
    string? logPath,
    Action exportSrm,
    Action restart,
    Action<Guid> stopClient,
    Action exit)
{
    public void Show(Point location, IntPtr owner, ServerStatus? status, string? serverError)
    {
        NativeMenu.Show(location, owner, BuildItems(status, serverError));
    }

    private List<NativeMenuItem> BuildItems(ServerStatus? status, string? serverError)
    {
        List<NativeMenuItem> items = [];
        if (!string.IsNullOrWhiteSpace(serverError))
        {
            items.Add(NativeMenuItem.Disabled(AppText.Header(serverError)));
            items.Add(NativeMenuItem.Separator);
        }

        items.AddRange(
        [
            NativeMenuItem.Action("Restart server", restart),
            CreateStartupItem(),
            NativeMenuItem.Action("Open settings", () => OpenFile(settingsPath)),
            CreateOpenLogsItem(),
            NativeMenuItem.Action("Export SRM manifest", exportSrm),
            NativeMenuItem.Separator,
            CreateClientsMenu(status, stopClient),
            CreateDiagnosticsMenu(status),
            NativeMenuItem.Separator,
            NativeMenuItem.Action("Exit", exit),
        ]);

        return items;
    }

    private static NativeMenuItem CreateStartupItem()
    {
        bool startupEnabled = StartupRegistration.IsEnabled();
        return NativeMenuItem.Action(
            "Start on startup",
            () => StartupRegistration.SetEnabled(!startupEnabled),
            isChecked: startupEnabled);
    }

    private NativeMenuItem CreateOpenLogsItem()
    {
        return string.IsNullOrWhiteSpace(logPath)
            ? NativeMenuItem.Disabled("Open logs (not configured)")
            : NativeMenuItem.Action("Open logs", () => OpenLogFile(logPath));
    }

    private static NativeMenuItem CreateClientsMenu(ServerStatus? status, Action<Guid> stopClient)
    {
        if (status is null || status.Runtime.Clients.Count == 0)
        {
            return NativeMenuItem.Menu("Clients", [NativeMenuItem.Disabled("none")]);
        }

        List<NativeMenuItem> items =
        [
            NativeMenuItem.Disabled($"connected: {status.ConnectedClientCount}"),
        ];
        foreach (ClientStatus client in status.Runtime.Clients)
        {
            items.Add(NativeMenuItem.Menu(
                $"{AppText.Active(client.IsActive)} {client.ProfileId}",
                [
                    NativeMenuItem.Action("Stop client", () => stopClient(client.ClientId)),
                    NativeMenuItem.Separator,
                    NativeMenuItem.Disabled($"pid: {client.ClientProcessId}"),
                    NativeMenuItem.Disabled($"steam app: {AppText.AppId(client.SteamAppId)}"),
                    NativeMenuItem.Disabled($"receivers: {AppText.Processes(client.ObservedProcesses)}"),
                    NativeMenuItem.Disabled($"blocked: {AppText.Processes(client.BlockedProcesses)}"),
                ]));
        }

        return NativeMenuItem.Menu("Clients", items);
    }

    private static NativeMenuItem CreateDiagnosticsMenu(ServerStatus? status)
    {
        return NativeMenuItem.Menu(
            "Diagnostics",
            [
                CreateInputsMenu(status),
                CreateOutputsMenu(status),
                CreateSteamInputMenu(status),
            ]);
    }

    private static NativeMenuItem CreateInputsMenu(ServerStatus? status)
    {
        if (status is null)
        {
            return NativeMenuItem.Menu("Inputs", [NativeMenuItem.Disabled("waiting for status")]);
        }

        PhysicalControllerPumpStatus physical = status.Inputs.PhysicalControllers;
        List<NativeMenuItem> items =
        [
            NativeMenuItem.Disabled($"controllers: {AppText.PhysicalSdl(physical)}"),
            NativeMenuItem.Disabled($"mouse: {AppText.MouseInput(status.Inputs.Mouse)}"),
        ];

        if (!string.IsNullOrWhiteSpace(physical.LastError))
        {
            items.Add(NativeMenuItem.Disabled($"controller error: {physical.LastError}"));
        }

        if (!string.IsNullOrWhiteSpace(status.Inputs.Mouse.LastError))
        {
            items.Add(NativeMenuItem.Disabled($"raw input error: {status.Inputs.Mouse.LastError}"));
        }

        foreach (string controller in physical.ControllerIds)
        {
            items.Add(NativeMenuItem.Disabled(controller));
        }

        return NativeMenuItem.Menu("Inputs", items);
    }

    private static NativeMenuItem CreateOutputsMenu(ServerStatus? status)
    {
        if (status is null)
        {
            return NativeMenuItem.Menu("Outputs", [NativeMenuItem.Disabled("waiting for status")]);
        }

        List<NativeMenuItem> items =
        [
            NativeMenuItem.Disabled($"controller output: {AppText.Enabled(status.Forwarding.ControllerOutputEnabled)}"),
            NativeMenuItem.Disabled($"motion: {AppText.Enabled(status.Forwarding.PhysicalMotionEnabled)}"),
            NativeMenuItem.Disabled($"mouse output: {AppText.FormatMouseOutput(status.MouseForwarding)}"),
            NativeMenuItem.Disabled($"pointer: {AppText.Enabled(status.MouseForwarding.PointerOutputEnabled)}"),
        ];

        if (status.Forwarding.Slots.Count == 0)
        {
            items.Add(NativeMenuItem.Disabled("controller slots: none"));
            return NativeMenuItem.Menu("Outputs", items);
        }

        foreach (ControllerSlotStatus slot in status.Forwarding.Slots)
        {
            items.Add(NativeMenuItem.Menu(
                AppText.ControllerSlot(slot),
                [
                    NativeMenuItem.Disabled($"output: {AppText.Output(slot.Output)}"),
                    NativeMenuItem.Disabled($"physical: {AppText.Connected(slot.HasPhysicalEndpoint)}"),
                    NativeMenuItem.Disabled($"client endpoints: {slot.ClientEndpointCount}"),
                    NativeMenuItem.Disabled($"active client: {AppText.YesNo(slot.HasActiveClientEndpoint)}"),
                ]));
        }

        return NativeMenuItem.Menu("Outputs", items);
    }

    private static NativeMenuItem CreateSteamInputMenu(ServerStatus? status)
    {
        if (status is null)
        {
            return NativeMenuItem.Menu("Steam Input", [NativeMenuItem.Disabled("waiting for status")]);
        }

        List<NativeMenuItem> items =
        [
            NativeMenuItem.Disabled($"forced: {AppText.YesNo(status.SteamInput.Forced)}"),
            NativeMenuItem.Disabled($"app id: {AppText.AppId(status.SteamInput.AppId)}"),
        ];

        if (!string.IsNullOrWhiteSpace(status.SteamInput.LastError))
        {
            items.Add(NativeMenuItem.Disabled($"error: {status.SteamInput.LastError}"));
        }

        return NativeMenuItem.Menu("Steam Input", items);
    }

    private static void OpenFile(string path)
    {
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = Path.GetFullPath(path),
            UseShellExecute = true,
        });
    }

    private static void OpenLogFile(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        using (File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
        }

        OpenFile(path);
    }
}
