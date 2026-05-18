using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VirtualMouse.Forwarding;
using VirtualMouse.Inputs.Sdl;

namespace VirtualMouse.Hosting;

internal sealed class PhysicalControllerPump(
    ControllerBroker broker,
    ILogger logger) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private IReadOnlyList<SdlGamepadSource> _sources = [];
    private Task? _task;
    private bool _disposed;

    public void Start(CancellationToken cancellationToken)
    {
        _task = Task.Run(() => RunLinked(cancellationToken), CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_task is not null)
        {
            try
            {
                await _task.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
            {
            }
        }

        foreach (SdlGamepadSource source in _sources)
        {
            await source.DisposeAsync().ConfigureAwait(false);
        }

        _stop.Dispose();
    }

    private void Run(CancellationToken cancellationToken)
    {
        try
        {
            _sources = SdlControllerCatalog.OpenPhysicalControllers();
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning("Physical SDL controller pump disabled: {Message}", exception.Message);
            return;
        }

        if (_sources.Count == 0)
        {
            logger.LogInformation("No physical SDL controllers found.");
            return;
        }

        logger.LogInformation("Physical SDL controller pump started: controllers={Count}", _sources.Count);
        SdlGamepadEventLoop.Run(_sources, UpdatePhysicalController, cancellationToken);
    }

    private void RunLinked(CancellationToken cancellationToken)
    {
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        Run(linked.Token);
    }

    private void UpdatePhysicalController(SdlGamepadSource source, ControllerState state)
    {
        broker.UpdatePhysicalController(
            new ControllerId(SdlControllerCatalog.GetPhysicalControllerId(source.Controller)),
            state,
            source.Features,
            source);
    }
}


internal sealed class NoopControllerOutputFactory : IControllerOutputFactory
{
    public IControllerOutput Connect(ControllerId controllerId, ControllerOutput output)
    {
        _ = controllerId;
        _ = output;
        return new NoopControllerOutput();
    }

    private sealed class NoopControllerOutput : IControllerOutput
    {
        public void Send(in ControllerState state)
        {
            _ = state;
        }

        public IDisposable ListenFeedback(Action<ControllerFeedback> handler)
        {
            _ = handler;
            return new Subscription();
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class NoopMouseOutputFactory : IMouseOutputFactory
{
    public IMouseOutput Connect(MouseOutput output)
    {
        _ = output;
        return new NoopMouseOutput();
    }

    private sealed class NoopMouseOutput : IMouseOutput
    {
        public bool IsConnected => true;

        public ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken = default)
        {
            _ = report;
            _ = cancellationToken;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class Subscription : IDisposable
{
    public void Dispose()
    {
    }
}
