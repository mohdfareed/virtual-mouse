using System;
using System.Threading;
using System.Threading.Tasks;
using Inputs;
using Inputs.Sdl;
using Outputs;
using Outputs.Viiper;
using Profiles;

namespace Hosting;

internal sealed class ControllerRoute : IAsyncDisposable
{
    private readonly Func<bool> _shouldForward;
    private readonly ControllerOutputKind _outputKind;
    private readonly ViiperXbox360Output? _xbox360;
    private readonly IDisposable? _feedback;
    private readonly ControllerRoutePipeServer _pipe;

    private ControllerRoute(
        Guid id,
        Guid runId,
        SdlControllerInfo controller,
        ControllerOutputKind outputKind,
        ViiperXbox360Output? xbox360,
        ControllerRoutePipeServer pipe,
        IDisposable? feedback,
        Func<bool> shouldForward)
    {
        Id = id;
        RunId = runId;
        Controller = controller;
        _outputKind = outputKind;
        _xbox360 = xbox360;
        _pipe = pipe;
        _feedback = feedback;
        _shouldForward = shouldForward;
    }

    public Guid Id { get; }

    public Guid RunId { get; }

    public SdlControllerInfo Controller { get; }

    public static async Task<ControllerRoute> CreateAsync(
        Guid runId,
        SdlControllerInfo controller,
        ControllerOutputKind outputKind,
        ViiperOptions viiper,
        Func<bool> shouldForward,
        CancellationToken cancellationToken)
    {
        Guid routeId = Guid.NewGuid();
        string pipeName = $"Hosting.ControllerRoute.{routeId:N}";
        ViiperXbox360Output? xbox360 = null;
        IDisposable? feedback = null;
        ControllerRoutePipeServer? pipe = null;
        bool created = false;

        try
        {
            if (outputKind == ControllerOutputKind.Xbox360)
            {
                await ViiperServer.EnsureRunningAsync(viiper, cancellationToken).ConfigureAwait(false);
                await ViiperXbox360Output.ReclaimOwnedDevicesAsync(viiper, cancellationToken).ConfigureAwait(false);
                xbox360 = await ViiperXbox360Output.ConnectAsync(viiper, cancellationToken).ConfigureAwait(false);
            }
            else if (outputKind == ControllerOutputKind.Ds4)
            {
                throw new NotSupportedException("DS4 output is not implemented yet.");
            }

            pipe = new ControllerRoutePipeServer(
                pipeName,
                state => HandleInput(shouldForward, outputKind, xbox360, state, cancellationToken));
            feedback = xbox360?.ListenRumble(rumble =>
            {
                pipe.SendFeedback(GamepadForwardingExtensions.ToGamepadRumble(rumble));
                return ValueTask.CompletedTask;
            });
            pipe.Start();

            ControllerRoute route = new(
                routeId,
                runId,
                controller,
                outputKind,
                xbox360,
                pipe,
                feedback,
                shouldForward);
            created = true;
            pipe = null;
            xbox360 = null;
            feedback = null;
            return route;
        }
        finally
        {
            if (!created)
            {
                feedback?.Dispose();
                if (pipe is not null)
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                }

                if (xbox360 is not null)
                {
                    await xbox360.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    public ControllerRouteInfo ToInfo()
    {
        return new ControllerRouteInfo(Id, RunId, Controller.Name, _pipe.PipeName);
    }

    public ControllerRouteStatus GetStatus()
    {
        return new ControllerRouteStatus(
            Id,
            RunId,
            Controller.Id,
            Controller.Name,
            Controller.Source,
            _outputKind,
            _shouldForward(),
            _xbox360?.IsConnected ?? false,
            _xbox360?.BusId,
            _xbox360?.DeviceId);
    }

    public async ValueTask DisposeAsync()
    {
        _feedback?.Dispose();
        await _pipe.DisposeAsync().ConfigureAwait(false);
        if (_xbox360 is not null)
        {
            await _xbox360.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void HandleInput(
        Func<bool> shouldForward,
        ControllerOutputKind outputKind,
        IXbox360Output? xbox360,
        GamepadState state,
        CancellationToken cancellationToken)
    {
        if (!shouldForward())
        {
            return;
        }

        if (outputKind == ControllerOutputKind.Xbox360 && xbox360 is not null)
        {
            GamepadForwardingExtensions.SendSynchronously(
                xbox360,
                GamepadForwardingExtensions.ToXbox360Report(state),
                cancellationToken);
        }
    }
}
