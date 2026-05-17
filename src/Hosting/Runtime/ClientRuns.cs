using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inputs.Sdl;
using Microsoft.Extensions.Logging;
using Outputs.Viiper;
using Profiles;

namespace Hosting;

internal sealed class ClientRunStore(
    IReadOnlyDictionary<string, GameProfile> profiles,
    ViiperOptions viiper,
    ForwardingHostState hostState,
    ILogger? logger = null,
    Func<int>? getForegroundProcessId = null,
    Func<int, int, bool>? isProcessInTree = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, ClientRun> _runs = [];
    private readonly Func<int> _getForegroundProcessId = getForegroundProcessId ?? DefaultGetForegroundProcessId;
    private readonly Func<int, int, bool> _isProcessInTree = isProcessInTree ?? DefaultIsProcessInTree;
    private Guid? _activeRunId;

    public async Task<ClientRunInfo> StartRunAsync(
        ClientRunRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ResolvedGameProfile profile = ProfileCatalog.Resolve(profiles, request.ProfileId);
        ClientRun run = new(Guid.NewGuid(), profile, request.ClientProcessId, request.SteamAppId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _runs.Add(run.Id, run);
            ClientRunLog.Started(logger, run.Id, run.Profile.Id);
            return run.ToInfo();
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async Task ActivateRunAsync(Guid runId, int rootProcessId)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            GetRun(runId).RootProcessId = rootProcessId > 0
                ? rootProcessId
                : throw new ArgumentOutOfRangeException(nameof(rootProcessId));
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async Task<ControllerRouteInfo> AttachControllerRouteAsync(
        Guid runId,
        SdlControllerInfo controller,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ControllerRoute? route = null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ClientRun run = GetRun(runId);
            route = await ControllerRoute.CreateAsync(
                    run.Id,
                    controller,
                    run.Profile.ControllerOutput,
                    viiper,
                    () => IsActiveRun(run.Id) && hostState.EmulationEnabled,
                    cancellationToken)
                .ConfigureAwait(false);
            run.Routes.Add(route.Id, route);
            ClientRunLog.RouteAttached(logger, run.Id, route.Id, controller.Name);
            ControllerRouteInfo info = route.ToInfo();
            route = null;
            return info;
        }
        finally
        {
            _ = _gate.Release();
            if (route is not null)
            {
                await route.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task EndRunAsync(Guid runId)
    {
        ClientRun? run = null;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_runs.TryGetValue(runId, out run))
            {
                return;
            }

            _ = _runs.Remove(runId);
            if (_activeRunId == runId)
            {
                _activeRunId = null;
            }
        }
        finally
        {
            _ = _gate.Release();
        }

        await run.DisposeAsync().ConfigureAwait(false);
        ClientRunLog.Ended(logger, runId, run.Profile.Id);
    }

    public async Task<ActiveRunState> RefreshActiveRunAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            int foregroundProcessId = _getForegroundProcessId();
            ClientRun? active = null;
            foreach (ClientRun run in _runs.Values)
            {
                if (run.RootProcessId is int rootProcessId &&
                    _isProcessInTree(rootProcessId, foregroundProcessId))
                {
                    active = run;
                    break;
                }
            }

            _activeRunId = active?.Id;
            return new ActiveRunState(
                active?.Id,
                active?.Profile.MouseOutput == MouseOutputKind.Viiper);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ClientRunStatus>> GetRunStatusAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            List<ClientRunStatus> statuses = new(_runs.Count);
            foreach (ClientRun run in _runs.Values)
            {
                statuses.Add(new ClientRunStatus(
                    run.Id,
                    run.Profile.Id,
                    run.Profile.Title,
                    _activeRunId == run.Id,
                    run.ClientProcessId,
                    run.RootProcessId,
                    run.SteamAppId,
                    run.Routes.Count));
            }

            return statuses;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ControllerRouteStatus>> GetRouteStatusAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            List<ControllerRouteStatus> statuses = [];
            foreach (ClientRun run in _runs.Values)
            {
                foreach (ControllerRoute route in run.Routes.Values)
                {
                    statuses.Add(route.GetStatus());
                }
            }

            return statuses;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        ClientRun[] runs;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            runs = [.. _runs.Values];
            _runs.Clear();
            _activeRunId = null;
        }
        finally
        {
            _ = _gate.Release();
        }

        foreach (ClientRun run in runs)
        {
            await run.DisposeAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
    }

    private bool IsActiveRun(Guid runId)
    {
        return _activeRunId == runId;
    }

    private ClientRun GetRun(Guid runId)
    {
        return _runs.TryGetValue(runId, out ClientRun? run)
            ? run
            : throw new InvalidOperationException($"Client run {runId} is not active.");
    }

    private static int DefaultGetForegroundProcessId()
    {
        return OperatingSystem.IsWindows()
            ? Platform.Windows.WindowsProcessInfo.GetForegroundProcessId()
            : 0;
    }

    private static bool DefaultIsProcessInTree(int rootProcessId, int processId)
    {
        return OperatingSystem.IsWindows() &&
            Platform.Windows.WindowsProcessInfo.IsProcessInTree(rootProcessId, processId);
    }
}

internal sealed class ClientRun(
    Guid id,
    ResolvedGameProfile profile,
    int clientProcessId,
    uint? steamAppId) : IAsyncDisposable
{
    public Guid Id { get; } = id;

    public ResolvedGameProfile Profile { get; } = profile;

    public int ClientProcessId { get; } = clientProcessId;

    public uint? SteamAppId { get; } = steamAppId;

    public int? RootProcessId { get; set; }

    public Dictionary<Guid, ControllerRoute> Routes { get; } = [];

    public ClientRunInfo ToInfo()
    {
        return new ClientRunInfo(
            Id,
            Profile.Id,
            Profile.Title,
            Profile.Executable,
            Profile.Arguments,
            Profile.WorkingDirectory,
            Profile.ReceiverProcesses,
            Profile.ControllerOutput,
            Profile.MouseOutput);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (ControllerRoute route in Routes.Values)
        {
            await route.DisposeAsync().ConfigureAwait(false);
        }

        Routes.Clear();
    }
}

/// <summary>Client run registration request.</summary>
public sealed record ClientRunRequest(
    string ProfileId,
    int ClientProcessId,
    uint? SteamAppId);

/// <summary>Resolved client run details returned to a client.</summary>
public sealed record ClientRunInfo(
    Guid RunId,
    string ProfileId,
    string Title,
    string Executable,
    string Arguments,
    string WorkingDirectory,
    IReadOnlyList<string> ReceiverProcesses,
    ControllerOutputKind ControllerOutput,
    MouseOutputKind MouseOutput);

/// <summary>Runtime client run status.</summary>
public readonly record struct ClientRunStatus(
    Guid RunId,
    string ProfileId,
    string Title,
    bool IsActive,
    int ClientProcessId,
    int? RootProcessId,
    uint? SteamAppId,
    int ControllerRoutes);

/// <summary>Controller route details returned to a client.</summary>
public readonly record struct ControllerRouteInfo(
    Guid RouteId,
    Guid RunId,
    string ControllerName,
    string PipeName)
{
    internal ControllerRoutePipeInfo Pipe => new(RouteId, PipeName);
}

/// <summary>Runtime controller route status.</summary>
public readonly record struct ControllerRouteStatus(
    Guid RouteId,
    Guid RunId,
    SdlControllerId ControllerId,
    string ControllerName,
    SdlControllerSource ControllerSource,
    ControllerOutputKind OutputKind,
    bool IsActive,
    bool OutputConnected,
    uint? OutputBusId,
    string? OutputDeviceId);

internal readonly record struct ActiveRunState(Guid? RunId, bool WantsMouse);

internal static class ClientRunLog
{
    private static readonly Action<ILogger, Guid, string, Exception?> StartedMessage =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(1, nameof(Started)),
            "Started client run {RunId} for {ProfileId}.");

    private static readonly Action<ILogger, Guid, string, Exception?> EndedMessage =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(2, nameof(Ended)),
            "Ended client run {RunId} for {ProfileId}.");

    private static readonly Action<ILogger, Guid, Guid, string, Exception?> RouteAttachedMessage =
        LoggerMessage.Define<Guid, Guid, string>(
            LogLevel.Information,
            new EventId(3, nameof(RouteAttached)),
            "Attached controller route {RouteId} to client run {RunId} for {ControllerName}.");

    public static void Started(ILogger? logger, Guid runId, string profileId)
    {
        if (logger is not null)
        {
            StartedMessage(logger, runId, profileId, null);
        }
    }

    public static void Ended(ILogger? logger, Guid runId, string profileId)
    {
        if (logger is not null)
        {
            EndedMessage(logger, runId, profileId, null);
        }
    }

    public static void RouteAttached(ILogger? logger, Guid runId, Guid routeId, string controllerName)
    {
        if (logger is not null)
        {
            RouteAttachedMessage(logger, runId, routeId, controllerName, null);
        }
    }
}
