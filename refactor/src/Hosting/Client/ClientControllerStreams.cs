using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VirtualMouse.Forwarding;
using VirtualMouse.Inputs.Sdl;

namespace VirtualMouse.Hosting;

internal sealed class ClientControllerStreams(ILogger logger) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _writeGate = new();
    private IReadOnlyList<SdlGamepadSource> _sources = [];
    private IReadOnlyDictionary<SdlGamepadSource, string> _physicalIds =
        new Dictionary<SdlGamepadSource, string>();
    private NamedPipeClientStream? _pipe;
    private ControllerPipeWriter? _writer;
    private Task? _inputTask;
    private Task? _feedbackTask;

    public async Task StartAsync(
        VirtualMouseClient client,
        ClientRunLaunch launch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(launch);

        try
        {
            IReadOnlyList<SdlControllerInfo> visibleControllers = SdlControllerCatalog.GetControllers();
            IReadOnlyList<SdlControllerInfo> physicalControllers = GetPhysicalControllers(visibleControllers);
            _sources = SdlControllerCatalog.OpenClientControllers();
            _physicalIds = CreatePhysicalIds(_sources, physicalControllers);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning("SDL controller streaming disabled: {Message}", exception.Message);
            await client.RegisterClientControllersAsync([], cancellationToken).ConfigureAwait(false);
            return;
        }

        ClientControllerInfo[] controllers = CreateControllerInfos(_sources, _physicalIds);
        await client.RegisterClientControllersAsync(controllers, cancellationToken)
            .ConfigureAwait(false);
        if (_sources.Count == 0)
        {
            return;
        }

        NamedPipeClientStream pipe = new(
            ".",
            launch.ControllerPipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
        _pipe = pipe;
        _writer = new ControllerPipeWriter(pipe);
        ControllerPipeReader reader = new(pipe);

        _inputTask = Task.Run(RunInputLoop, CancellationToken.None);
        _feedbackTask = Task.Run(() => RunFeedbackLoopAsync(reader), CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _pipe?.Dispose();
        await IgnoreExpectedStopAsync(_inputTask).ConfigureAwait(false);
        await IgnoreExpectedStopAsync(_feedbackTask).ConfigureAwait(false);

        foreach (SdlGamepadSource source in _sources)
        {
            await source.DisposeAsync().ConfigureAwait(false);
        }

        _stop.Dispose();
    }

    private void RunInputLoop()
    {
        SdlGamepadEventLoop.Run(_sources, SendInput, _stop.Token);
    }

    private async Task RunFeedbackLoopAsync(ControllerPipeReader reader)
    {
        while (!_stop.IsCancellationRequested)
        {
            ControllerPipeMessage message = await reader.ReadAsync(_stop.Token).ConfigureAwait(false);
            if (message.Type == ControllerPipeFrameType.Feedback &&
                message.Feedback.ControllerIndex < _sources.Count)
            {
                _ = _sources[message.Feedback.ControllerIndex].TrySendFeedback(message.Feedback.Feedback);
            }
        }
    }

    private void SendInput(
        SdlGamepadSource source,
        ControllerState state)
    {
        ControllerPipeWriter? writer = _writer;
        if (writer is null || _stop.IsCancellationRequested)
        {
            return;
        }

        ushort controllerIndex = FindSourceIndex(source);
        lock (_writeGate)
        {
            writer.WriteInputAsync(new ControllerInputFrame(controllerIndex, state))
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
    }

    private static ClientControllerInfo[] CreateControllerInfos(
        IReadOnlyList<SdlGamepadSource> sources,
        IReadOnlyDictionary<SdlGamepadSource, string> physicalIds)
    {
        ClientControllerInfo[] controllers = new ClientControllerInfo[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            SdlGamepadSource source = sources[i];
            controllers[i] = new ClientControllerInfo(
                checked((ushort)i),
                physicalIds[source],
                source.Features);
        }

        return controllers;
    }

    private static Dictionary<SdlGamepadSource, string> CreatePhysicalIds(
        IReadOnlyList<SdlGamepadSource> sources,
        IReadOnlyList<SdlControllerInfo> physicalControllers)
    {
        Dictionary<SdlGamepadSource, string> ids = [];
        foreach (SdlGamepadSource source in sources)
        {
            SdlControllerInfo controller = source.Controller;
            SdlControllerInfo? physical = SdlControllerMatcher.FindPhysicalController(
                controller,
                physicalControllers);
            ids[source] = SdlControllerCatalog.GetPhysicalControllerId(physical ?? controller);
        }

        return ids;
    }

    private static List<SdlControllerInfo> GetPhysicalControllers(
        IReadOnlyList<SdlControllerInfo> controllers)
    {
        List<SdlControllerInfo> physical = [];
        foreach (SdlControllerInfo controller in controllers)
        {
            if (controller.Source == SdlControllerSource.Physical)
            {
                physical.Add(controller);
            }
        }

        return physical;
    }

    private ushort FindSourceIndex(SdlGamepadSource source)
    {
        for (int i = 0; i < _sources.Count; i++)
        {
            if (ReferenceEquals(_sources[i], source))
            {
                return checked((ushort)i);
            }
        }

        return 0;
    }

    private static async Task IgnoreExpectedStopAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
    }
}
