using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Hosting;

internal sealed class MouseRouteController(
    string routeId,
    Func<CancellationToken, Task<IForwardingRoute>> createRouteAsync,
    ILogger? logger,
    Func<bool>? shouldForward = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MouseRouteInstance? _instance;
    private bool _wanted;

    public async Task SetWantedAsync(bool wanted, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_wanted == wanted)
            {
                return;
            }

            _wanted = wanted;
            if (wanted)
            {
                _instance = await MouseRouteInstance.StartAsync(
                    createRouteAsync,
                    logger,
                    shouldForward,
                    cancellationToken)
                .ConfigureAwait(false);
                return;
            }

            MouseRouteInstance? instance = _instance;
            _instance = null;
            if (instance is not null)
            {
                await instance.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async Task<ForwardingRouteStatus> GetStatusAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            return new ForwardingRouteStatus(
                routeId,
                _instance?.Host.IsConnected ?? false,
                _wanted ? 1 : 0);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        MouseRouteInstance? instance;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _wanted = false;
            instance = _instance;
            _instance = null;
        }
        finally
        {
            _ = _gate.Release();
        }

        if (instance is not null)
        {
            await instance.DisposeAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
    }

    private sealed class MouseRouteInstance(
        ForwardingHost host,
        IDisposable enableLease,
        CancellationTokenSource cancellationSource,
        Task runTask) : IAsyncDisposable
    {
        public ForwardingHost Host { get; } = host;

        public static async Task<MouseRouteInstance> StartAsync(
            Func<CancellationToken, Task<IForwardingRoute>> createRouteAsync,
            ILogger? logger,
            Func<bool>? shouldForward,
            CancellationToken cancellationToken)
        {
#pragma warning disable CA2000
            IForwardingRoute route = await createRouteAsync(cancellationToken).ConfigureAwait(false);
            ForwardingHost host = new(route, logger, shouldForward);

            try
            {
                IDisposable enableLease = host.Enable();

                try
                {
                    CancellationTokenSource runCancellation = new();

                    try
                    {
                        Task runTask = Task.Run(() => host.Run(runCancellation.Token), CancellationToken.None);
                        return new MouseRouteInstance(host, enableLease, runCancellation, runTask);
                    }
                    catch
                    {
                        runCancellation.Dispose();
                        throw;
                    }
                }
                catch
                {
                    enableLease.Dispose();
                    throw;
                }
            }
            catch
            {
                await host.DisposeAsync().ConfigureAwait(false);
                throw;
            }
#pragma warning restore CA2000
        }

        public async ValueTask DisposeAsync()
        {
            await cancellationSource.CancelAsync().ConfigureAwait(false);

            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
            {
            }
            catch (IOException) when (cancellationSource.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (cancellationSource.IsCancellationRequested)
            {
            }
            catch (InvalidOperationException) when (cancellationSource.IsCancellationRequested)
            {
            }
            finally
            {
                enableLease.Dispose();
                cancellationSource.Dispose();
                await Host.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

internal sealed class ForwardingHostRuntime : IAsyncDisposable
{
    private static readonly TimeSpan ActivePollInterval = TimeSpan.FromMilliseconds(250);
    private readonly MouseRouteController _mouse;
    private readonly ClientRunStore _runs;
    private readonly ForwardingHostState _hostState;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _stopActiveMonitor = new();
    private readonly Task _activeMonitorTask;

    public ForwardingHostRuntime(
        MouseRouteController mouse,
        ClientRunStore runs,
        ForwardingHostState hostState,
        ILogger? logger = null)
    {
        _mouse = mouse;
        _runs = runs;
        _hostState = hostState;
        _logger = logger;
        _activeMonitorTask = Task.Run(RunActiveMonitorAsync, CancellationToken.None);
    }

    public Task<ForwardingHostStatus> GetStatusAsync()
    {
        return GetStatusCoreAsync();
    }

    public Task SetMouseWantedAsync(bool wanted, CancellationToken cancellationToken)
    {
        return _mouse.SetWantedAsync(wanted, cancellationToken);
    }

    public Task<ClientRunInfo> StartRunAsync(
        ClientRunRequest request,
        CancellationToken cancellationToken)
    {
        return _runs.StartRunAsync(request, cancellationToken);
    }

    public Task ActivateRunAsync(Guid runId, int rootProcessId)
    {
        return _runs.ActivateRunAsync(runId, rootProcessId);
    }

    public Task<ControllerRouteInfo> AttachControllerRouteAsync(
        Guid runId,
        Inputs.Sdl.SdlControllerInfo controller,
        CancellationToken cancellationToken)
    {
        return _runs.AttachControllerRouteAsync(runId, controller, cancellationToken);
    }

    public Task EndRunAsync(Guid runId)
    {
        return _runs.EndRunAsync(runId);
    }

    public Task SetEmulationEnabledAsync(bool enabled)
    {
        _hostState.SetEmulationEnabled(enabled);
        return Task.CompletedTask;
    }

    public Task<bool> ToggleEmulationEnabledAsync()
    {
        return Task.FromResult(_hostState.ToggleEmulationEnabled());
    }

    public Task SetPhysicalMotionEnabledAsync(bool enabled)
    {
        _hostState.SetPhysicalMotionEnabled(enabled);
        return Task.CompletedTask;
    }

    public Task<bool> TogglePhysicalMotionEnabledAsync()
    {
        return Task.FromResult(_hostState.TogglePhysicalMotionEnabled());
    }

    public async ValueTask DisposeAsync()
    {
        await _stopActiveMonitor.CancelAsync().ConfigureAwait(false);
        try
        {
            await _activeMonitorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopActiveMonitor.IsCancellationRequested)
        {
        }

        _stopActiveMonitor.Dispose();
        await _mouse.DisposeAsync().ConfigureAwait(false);
        await _runs.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<ForwardingHostStatus> GetStatusCoreAsync()
    {
        ForwardingRouteStatus mouseStatus = await _mouse.GetStatusAsync().ConfigureAwait(false);
        IReadOnlyList<ControllerRouteStatus> controllerRoutes =
            await _runs.GetRouteStatusAsync().ConfigureAwait(false);
        IReadOnlyList<ClientRunStatus> clientRuns =
            await _runs.GetRunStatusAsync().ConfigureAwait(false);

        return new ForwardingHostStatus(
            mouseStatus,
            controllerRoutes,
            clientRuns,
            _hostState.EmulationEnabled,
            _hostState.PhysicalMotionEnabled);
    }

    private async Task RunActiveMonitorAsync()
    {
        while (!_stopActiveMonitor.IsCancellationRequested)
        {
            try
            {
                ActiveRunState active = await _runs
                    .RefreshActiveRunAsync()
                    .ConfigureAwait(false);
                await _mouse
                    .SetWantedAsync(active.WantsMouse, _stopActiveMonitor.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stopActiveMonitor.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031
            catch (Exception exception)
#pragma warning restore CA1031
            {
                ForwardingHostRuntimeLog.ActiveMonitorFailed(_logger, exception);
            }

            await Task.Delay(ActivePollInterval, _stopActiveMonitor.Token).ConfigureAwait(false);
        }
    }
}

internal static class ForwardingHostRuntimeLog
{
    private static readonly Action<ILogger, Exception?> ActiveMonitorFailedMessage =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, nameof(ActiveMonitorFailed)),
            "Active profile monitor iteration failed.");

    public static void ActiveMonitorFailed(ILogger? logger, Exception exception)
    {
        if (logger is not null)
        {
            ActiveMonitorFailedMessage(logger, exception);
        }
    }
}
