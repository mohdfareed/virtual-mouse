using System;
using System.IO;
using System.Threading.Tasks;
using Steamworks;
using VirtualMouse.SteamInput;
using SteamworksInput = Steamworks.SteamInput;

internal sealed class SteamInputBench : IAsyncDisposable
{
    private const int MaxControllers = 16;
    private const string ActionSetName = "Mouse";
    private const string LeftActionName = "MouseLeft";
    private SteamInputVirtualMouse? session;
    private bool previousLeft;

    // MARK: Commands
    // ========================================================================

    public async Task InitializeAsync()
    {
        await DisposeAsync().ConfigureAwait(false);
        session = await SteamInputVirtualMouse
            .ConnectAsync(new SteamInputOptions(InitializeSteamApi: true, GetManifestPath()))
            .ConfigureAwait(false);

        InputActionSetHandle_t actionSet = SteamworksInput.GetActionSetHandle(ActionSetName);
        InputDigitalActionHandle_t leftAction = SteamworksInput.GetDigitalActionHandle(LeftActionName);

        await Console.Out.WriteLineAsync("steam input initialized").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"manifest    {GetManifestPath()}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"action set  {ActionSetName} ({actionSet.m_InputActionSetHandle})").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"left click  {LeftActionName} ({leftAction.m_InputDigitalActionHandle})").ConfigureAwait(false);
    }

    public async Task WatchLeftAsync()
    {
        if (session is null)
        {
            await InitializeAsync().ConfigureAwait(false);
        }

        InputActionSetHandle_t actionSet = SteamworksInput.GetActionSetHandle(ActionSetName);
        InputDigitalActionHandle_t leftAction = SteamworksInput.GetDigitalActionHandle(LeftActionName);
        if (actionSet.m_InputActionSetHandle == 0 || leftAction.m_InputDigitalActionHandle == 0)
        {
            await Console.Out.WriteLineAsync("missing action handles; manifest did not load correctly.").ConfigureAwait(false);
            return;
        }

        await Console.Out.WriteLineAsync("watching MouseLeft. Press Enter to stop.").ConfigureAwait(false);
        Task<string?> stopTask = Console.In.ReadLineAsync();

        while (!stopTask.IsCompleted)
        {
            SteamAPI.RunCallbacks();
            SteamworksInput.RunFrame(false);
            PollLeft(actionSet, leftAction);
            await Task.Delay(8).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }

        session = null;
    }

    // MARK: Helpers
    // ========================================================================

    private void PollLeft(InputActionSetHandle_t actionSet, InputDigitalActionHandle_t leftAction)
    {
        InputHandle_t[] controllers = new InputHandle_t[MaxControllers];
        int count = SteamworksInput.GetConnectedControllers(controllers);
        if (count == 0)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            InputHandle_t controller = controllers[i];
            SteamworksInput.ActivateActionSet(controller, actionSet);

            InputDigitalActionData_t data = SteamworksInput.GetDigitalActionData(controller, leftAction);
            bool active = data.bActive != 0;
            bool pressed = data.bState != 0;
            if (!active || pressed == previousLeft)
            {
                continue;
            }

            previousLeft = pressed;
            Console.WriteLine($"controller={controller.m_InputHandle} MouseLeft={(pressed ? "pressed" : "released")}");
        }
    }

    private static string GetManifestPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "steam_input_manifest.vdf");
    }
}
