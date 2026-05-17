using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Inputs.RawInput;
using Inputs.Sdl;
using Outputs.Viiper;

namespace Hosting;

internal static class ForwardingHostRuntimeFactory
{
    public static ForwardingHostRuntime Create(ForwardingServerOptions options)
    {
#pragma warning disable CA2000
        SdlDeviceSelection xpadDeviceSelection = ValidateSdlOptions(options.SdlGamepad);
        ForwardingHostState hostState = new();
        HostedRouteController mouse = new(
            ForwardingRouteIds.Mouse,
            ct => CreateMouseRouteAsync(options.Viiper, ct),
            options.Logger,
            () => hostState.EmulationEnabled);
        HostedRouteController xpad = new(
            ForwardingRouteIds.Xpad,
            ct => CreateXpadRouteAsync(options.Viiper, options.SdlGamepad, hostState, ct),
            options.Logger,
            () => hostState.EmulationEnabled);

        return new ForwardingHostRuntime(
            mouse,
            xpad,
            options.SdlGamepad.DeviceIndex,
            options.SdlGamepad.Mode,
            options.SdlGamepad.UsePhysicalMotion,
            hostState,
            xpadDeviceSelection.DeviceName,
            xpadDeviceSelection.MotionDeviceIndex,
            xpadDeviceSelection.MotionDeviceName);
#pragma warning restore CA2000
    }

    private static SdlDeviceSelection ValidateSdlOptions(SdlGamepadOptions options)
    {
        if (options.UsePhysicalMotion && options.Mode != SdlGamepadInputMode.Steam)
        {
            throw new InvalidOperationException("SDL physical motion requires xpad mode steam.");
        }

        IReadOnlyList<SdlGamepadInfo> gamepads = SdlGamepadSource.GetGamepads();
        int deviceIndex = options.DeviceIndex;
        if (deviceIndex < 0 || deviceIndex >= gamepads.Count)
        {
            throw new InvalidOperationException($"SDL gamepad index {deviceIndex} is not available.");
        }

        SdlGamepadInfo gamepad = gamepads[deviceIndex];
        ValidateSdlMode(gamepad, options.Mode);

        if (!options.UsePhysicalMotion)
        {
            return new SdlDeviceSelection(gamepad.Name, null, null);
        }

        int motionDeviceIndex = SdlGamepadSource.ResolveMotionDeviceIndex(gamepads, gamepad, options);
        if (motionDeviceIndex < 0 || motionDeviceIndex >= gamepads.Count)
        {
            throw new InvalidOperationException($"SDL motion gamepad index {motionDeviceIndex} is not available.");
        }

        SdlGamepadInfo motionGamepad = gamepads[motionDeviceIndex];
        ValidatePhysicalSdlGamepad(motionGamepad);
        return new SdlDeviceSelection(gamepad.Name, motionDeviceIndex, motionGamepad.Name);
    }

    private static void ValidateSdlMode(SdlGamepadInfo gamepad, SdlGamepadInputMode mode)
    {
        bool expectsSteamInput = mode switch
        {
            SdlGamepadInputMode.Physical => false,
            SdlGamepadInputMode.Steam => true,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        if (gamepad.IsSteamInput != expectsSteamInput)
        {
            throw new InvalidOperationException(
                $"SDL gamepad index {gamepad.Index} does not match xpad mode {mode}.");
        }
    }

    private static void ValidatePhysicalSdlGamepad(SdlGamepadInfo gamepad)
    {
        if (gamepad.IsSteamInput)
        {
            throw new InvalidOperationException(
                $"SDL motion gamepad index {gamepad.Index} is not a physical SDL gamepad.");
        }
    }

    private static Task<IForwardingRoute> CreateMouseRouteAsync(
        ViiperOptions viiperOptions,
        CancellationToken cancellationToken)
    {
        return OperatingSystem.IsWindows()
            ? CreateWindowsMouseRouteAsync(viiperOptions, cancellationToken)
            : throw new PlatformNotSupportedException("Mouse host routes require Windows.");
    }

    [SupportedOSPlatform("windows")]
    private static async Task<IForwardingRoute> CreateWindowsMouseRouteAsync(
        ViiperOptions viiperOptions,
        CancellationToken cancellationToken)
    {
        RawInputMouseSource? input = null;
        ViiperMouseOutput? output = null;

        try
        {
            input = await RawInputMouseSource.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await ViiperServer.EnsureRunningAsync(viiperOptions, cancellationToken).ConfigureAwait(false);
            output = await ViiperMouseOutput.ConnectAsync(viiperOptions, cancellationToken).ConfigureAwait(false);

            MouseForwardingRoute route = new(input, output);
            input = null;
            output = null;
            return route;
        }
        finally
        {
            if (input is not null)
            {
                await input.DisposeAsync().ConfigureAwait(false);
            }

            if (output is not null)
            {
                await output.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<IForwardingRoute> CreateXpadRouteAsync(
        ViiperOptions viiperOptions,
        SdlGamepadOptions sdlOptions,
        ForwardingHostState hostState,
        CancellationToken cancellationToken)
    {
        SdlGamepadSource? input = null;
        ViiperXbox360Output? output = null;

        try
        {
            input = await SdlGamepadSource.ConnectAsync(sdlOptions, cancellationToken).ConfigureAwait(false);
            await ViiperServer.EnsureRunningAsync(viiperOptions, cancellationToken).ConfigureAwait(false);
            output = await ViiperXbox360Output.ConnectAsync(viiperOptions, cancellationToken).ConfigureAwait(false);

            Xbox360ForwardingRoute route = new(
                input,
                output,
                shouldForwardMotion: () => hostState.PhysicalMotionEnabled);
            input = null;
            output = null;
            return route;
        }
        finally
        {
            if (input is not null)
            {
                await input.DisposeAsync().ConfigureAwait(false);
            }

            if (output is not null)
            {
                await output.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private readonly record struct SdlDeviceSelection(
        string? DeviceName,
        int? MotionDeviceIndex,
        string? MotionDeviceName);
}
