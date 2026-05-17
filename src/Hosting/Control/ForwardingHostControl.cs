using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Inputs.Sdl;
using Microsoft.Extensions.Logging;
using PolyType;
using StreamJsonRpc;

namespace Hosting;

/// <summary>Serves local forwarding control commands.</summary>
internal sealed class ForwardingHostServer(
    ForwardingHostRuntime runtime,
    string pipeName,
    Action? requestStop = null,
    ILogger? logger = null)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ForwardingHostControlLog.StartingServer(logger, pipeName);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CA2000
            NamedPipeServerStream pipe = CreatePipe();
#pragma warning restore CA2000
            using CancellationTokenRegistration registration = cancellationToken.Register(static target =>
            {
                ((NamedPipeServerStream)target!).Dispose();
            }, pipe);

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            NamedPipeServerStream connectedPipe = pipe;
            pipe = null!;
            _ = Task.Run(async () =>
            {
                try
                {
                    using (connectedPipe)
                    {
                        await HandleConnectionAsync(connectedPipe, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch (IOException exception)
                {
                    ForwardingHostControlLog.ConnectionClosed(logger, exception);
                }
            }, CancellationToken.None);
        }
    }

    private NamedPipeServerStream CreatePipe()
    {
        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 254,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    private async Task HandleConnectionAsync(Stream stream, CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration streamRegistration = cancellationToken.Register(static target =>
        {
            ((Stream)target!).Dispose();
        }, stream);

        ForwardingHostControlConnection target = new(runtime, requestStop, logger);
        using JsonRpc rpc = JsonRpc.Attach(stream, target);

        try
        {
            await rpc.Completion.ConfigureAwait(false);
        }
        finally
        {
            target.Dispose();
        }
    }
}

[JsonRpcContract]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
internal partial interface IForwardingHostControl
{
    Task<ForwardingHostStatus> GetStatusAsync();

    Task EnableMouseAsync();

    Task DisableMouseAsync();

    Task SetEmulationEnabledAsync(bool enabled);

    Task<bool> ToggleEmulationEnabledAsync();

    Task SetPhysicalMotionEnabledAsync(bool enabled);

    Task<bool> TogglePhysicalMotionEnabledAsync();

    Task<ClientRunInfo> StartRunAsync(ClientRunRequest request);

    Task ActivateRunAsync(Guid runId, int rootProcessId);

    Task<ControllerRouteInfo> AttachControllerRouteAsync(Guid runId, SdlControllerInfo controller);

    Task EndRunAsync(Guid runId);

    Task StopAsync();
}

internal sealed class ForwardingHostControlConnection(
    ForwardingHostRuntime runtime,
    Action? requestStop,
    ILogger? logger) : IForwardingHostControl, IDisposable
{
    private bool _mouseWanted;
    private readonly List<Guid> _clientRuns = [];

    public Task<ForwardingHostStatus> GetStatusAsync()
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(GetStatusAsync));
        return runtime.GetStatusAsync();
    }

    public async Task EnableMouseAsync()
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(EnableMouseAsync));
        _mouseWanted = true;
        await runtime.SetMouseWantedAsync(wanted: true, CancellationToken.None).ConfigureAwait(false);
        ForwardingHostControlLog.RouteWanted(logger, ForwardingRouteIds.Mouse);
    }

    public async Task DisableMouseAsync()
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(DisableMouseAsync));
        _mouseWanted = false;
        await runtime.SetMouseWantedAsync(wanted: false, CancellationToken.None).ConfigureAwait(false);
    }

    public Task SetEmulationEnabledAsync(bool enabled)
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(SetEmulationEnabledAsync));
        return runtime.SetEmulationEnabledAsync(enabled);
    }

    public Task<bool> ToggleEmulationEnabledAsync()
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(ToggleEmulationEnabledAsync));
        return runtime.ToggleEmulationEnabledAsync();
    }

    public Task SetPhysicalMotionEnabledAsync(bool enabled)
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(SetPhysicalMotionEnabledAsync));
        return runtime.SetPhysicalMotionEnabledAsync(enabled);
    }

    public Task<bool> TogglePhysicalMotionEnabledAsync()
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(TogglePhysicalMotionEnabledAsync));
        return runtime.TogglePhysicalMotionEnabledAsync();
    }

    public async Task<ClientRunInfo> StartRunAsync(ClientRunRequest request)
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(StartRunAsync));
        ClientRunInfo run = await runtime
            .StartRunAsync(request, CancellationToken.None)
            .ConfigureAwait(false);
        _clientRuns.Add(run.RunId);
        return run;
    }

    public Task ActivateRunAsync(Guid runId, int rootProcessId)
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(ActivateRunAsync));
        return runtime.ActivateRunAsync(runId, rootProcessId);
    }

    public Task<ControllerRouteInfo> AttachControllerRouteAsync(
        Guid runId,
        SdlControllerInfo controller)
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(AttachControllerRouteAsync));
        return runtime.AttachControllerRouteAsync(runId, controller, CancellationToken.None);
    }

    public Task EndRunAsync(Guid runId)
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(EndRunAsync));
        _ = _clientRuns.Remove(runId);
        return runtime.EndRunAsync(runId);
    }

    public Task StopAsync()
    {
        ForwardingHostControlLog.ReceivedCommand(logger, nameof(StopAsync));
        requestStop?.Invoke();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_mouseWanted)
        {
            runtime.SetMouseWantedAsync(wanted: false, CancellationToken.None).GetAwaiter().GetResult();
            _mouseWanted = false;
        }

        foreach (Guid runId in _clientRuns)
        {
            runtime.EndRunAsync(runId).GetAwaiter().GetResult();
        }

        _clientRuns.Clear();
    }
}

internal static class ForwardingHostControlLog
{
    private static readonly Action<ILogger, string, Exception?> StartingServerMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, nameof(StartingServer)),
            "Starting host control server on pipe {PipeName}.");

    private static readonly Action<ILogger, Exception?> ConnectionClosedMessage =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2, nameof(ConnectionClosed)),
            "Host control connection closed.");

    private static readonly Action<ILogger, string, Exception?> ReceivedCommandMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(3, nameof(ReceivedCommand)),
            "Received host control command {Command}.");

    private static readonly Action<ILogger, string, Exception?> RouteWantedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(4, nameof(RouteWanted)),
            "Host route {RouteId} requested.");

    public static void StartingServer(ILogger? logger, string pipeName)
    {
        if (logger is not null)
        {
            StartingServerMessage(logger, pipeName, null);
        }
    }

    public static void ConnectionClosed(ILogger? logger, Exception exception)
    {
        if (logger is not null)
        {
            ConnectionClosedMessage(logger, exception);
        }
    }

    public static void ReceivedCommand(ILogger? logger, string command)
    {
        if (logger is not null)
        {
            ReceivedCommandMessage(logger, command, null);
        }
    }

    public static void RouteWanted(ILogger? logger, string routeId)
    {
        if (logger is not null)
        {
            RouteWantedMessage(logger, routeId, null);
        }
    }
}
