using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Outputs.Viiper;
using Profiles;

namespace Hosting.Tests;

#pragma warning disable CA1416
#pragma warning disable CA2000
#pragma warning disable CA2007

/// <summary>Tests for local forwarding host behavior.</summary>
[TestClass]
public sealed class ForwardingHostTests
{
    /// <summary>Checks disabled forwarding.</summary>
    [TestMethod]
    public async Task DisabledHostDropsInput()
    {
        MouseReport report = new(MouseButtons.None, 1, 0, 0);
        TestMouseInputSource input = new(new MouseInput(report, "device"));
        TestMouseOutput output = new();
        await using ForwardingHost host = new(new MouseForwardingRoute(input, output));

        host.Run();

        Assert.HasCount(0, output.Reports);
    }

    /// <summary>Checks enabled forwarding.</summary>
    [TestMethod]
    public async Task EnabledHostForwardsInput()
    {
        MouseReport report = new(MouseButtons.None, 1, 0, 0);
        TestMouseInputSource input = new(new MouseInput(report, "device"));
        TestMouseOutput output = new();
        await using ForwardingHost host = new(new MouseForwardingRoute(input, output));

        using (host.Enable())
        {
            host.Run();
        }

        Assert.HasCount(1, output.Reports);
        Assert.AreEqual(report, output.Reports[0]);
    }

    /// <summary>Checks that state changes affect later reports.</summary>
    [TestMethod]
    public async Task DisabledStateStopsLaterReports()
    {
        MouseReport report = new(MouseButtons.None, 1, 0, 0);
        TestMouseInputSource input = new(
            new MouseInput(report, "device"),
            new MouseInput(report, "device"));
        TestMouseOutput output = new();
        await using ForwardingHost host = new(new MouseForwardingRoute(input, output));
        IDisposable? lease = null;
        input.BeforeReport = index =>
        {
            if (index == 1)
            {
                lease?.Dispose();
            }
        };

        using (lease = host.Enable())
        {
            host.Run();
        }

        Assert.HasCount(1, output.Reports);
    }

    /// <summary>Checks control connection status.</summary>
    [TestMethod]
    public async Task ControlSessionReportsHostState()
    {
        await using ForwardingHostRuntime runtime = CreateRuntime();
        using ForwardingHostControlConnection connection = new(runtime, requestStop: null, logger: null);

        ForwardingHostStatus status = await connection.GetStatusAsync().ConfigureAwait(false);

        Assert.AreEqual("mouse", status.Mouse.RouteId);
        Assert.AreEqual(0, status.Mouse.EnabledClientCount);
        Assert.IsFalse(status.Mouse.IsConnected);
        Assert.HasCount(0, status.ControllerRoutes);
        Assert.HasCount(0, status.ClientRuns);
        Assert.IsTrue(status.EmulationEnabled);
        Assert.IsTrue(status.PhysicalMotionEnabled);
    }

    /// <summary>Checks lease-counted enable state.</summary>
    [TestMethod]
    public async Task EnableLeasesKeepForwardingUntilLastLeaseDisposes()
    {
        await using ForwardingHost host = CreateHost();
        using IDisposable leaseA = host.Enable();
        using IDisposable leaseB = host.Enable();

        Assert.IsTrue(host.IsEnabled);
        Assert.AreEqual(2, host.EnabledLeaseCount);

        leaseA.Dispose();

        Assert.IsTrue(host.IsEnabled);
        Assert.AreEqual(1, host.EnabledLeaseCount);

        leaseB.Dispose();

        Assert.IsFalse(host.IsEnabled);
        Assert.AreEqual(0, host.EnabledLeaseCount);
    }

    /// <summary>Checks control connection enable cleanup.</summary>
    [TestMethod]
    public async Task ControlConnectionEnableCleanupRunsOnDispose()
    {
        await using ForwardingHostRuntime runtime = CreateRuntime();
        using ForwardingHostControlConnection connection = new(runtime, requestStop: null, logger: null);

        await connection.EnableMouseAsync().ConfigureAwait(false);

        ForwardingHostStatus enabled = await connection.GetStatusAsync().ConfigureAwait(false);
        Assert.AreEqual(1, enabled.Mouse.EnabledClientCount);
        Assert.IsTrue(enabled.Mouse.IsConnected);

        connection.Dispose();

        ForwardingHostStatus disabled = await runtime.GetStatusAsync().ConfigureAwait(false);
        Assert.AreEqual(0, disabled.Mouse.EnabledClientCount);
        Assert.IsFalse(disabled.Mouse.IsConnected);
    }

    /// <summary>Checks control connection route disabling without disconnecting.</summary>
    [TestMethod]
    public async Task ControlConnectionDisableReleasesMouseWithoutDisconnecting()
    {
        await using ForwardingHostRuntime runtime = CreateRuntime();
        using ForwardingHostControlConnection connection = new(runtime, requestStop: null, logger: null);

        await connection.EnableMouseAsync().ConfigureAwait(false);
        await connection.DisableMouseAsync().ConfigureAwait(false);

        ForwardingHostStatus disabled = await connection.GetStatusAsync().ConfigureAwait(false);
        Assert.AreEqual(0, disabled.Mouse.EnabledClientCount);
        Assert.IsFalse(disabled.Mouse.IsConnected);
    }

    /// <summary>Checks global host state can change without owning a client run.</summary>
    [TestMethod]
    public async Task ControlSessionUpdatesGlobalState()
    {
        await using ForwardingHostRuntime runtime = CreateRuntime();
        using ForwardingHostControlConnection connection = new(runtime, requestStop: null, logger: null);

        await connection.SetEmulationEnabledAsync(false).ConfigureAwait(false);
        await connection.SetPhysicalMotionEnabledAsync(false).ConfigureAwait(false);

        ForwardingHostStatus disabled = await connection.GetStatusAsync().ConfigureAwait(false);
        Assert.IsFalse(disabled.EmulationEnabled);
        Assert.IsFalse(disabled.PhysicalMotionEnabled);

        bool emulationToggled = await connection.ToggleEmulationEnabledAsync().ConfigureAwait(false);
        bool physicalMotionToggled = await connection.TogglePhysicalMotionEnabledAsync().ConfigureAwait(false);

        ForwardingHostStatus enabled = await connection.GetStatusAsync().ConfigureAwait(false);
        Assert.IsTrue(emulationToggled);
        Assert.IsTrue(physicalMotionToggled);
        Assert.IsTrue(enabled.EmulationEnabled);
        Assert.IsTrue(enabled.PhysicalMotionEnabled);
    }

    /// <summary>Checks client run registration and default receiver process resolution.</summary>
    [TestMethod]
    public async Task StartRunResolvesConfiguredProfile()
    {
        Dictionary<string, GameProfile> profiles = new(StringComparer.OrdinalIgnoreCase)
        {
            ["test-game"] = new GameProfile
            {
                Executable = @"C:\Games\TestGame\Game.exe",
                ControllerOutput = ControllerOutputKind.None,
                MouseOutput = MouseOutputKind.None,
            },
        };
        await using ForwardingHostRuntime runtime = CreateRuntime(profiles);

        ClientRunInfo run = await runtime
            .StartRunAsync(new ClientRunRequest("test-game", 123, 456), CancellationToken.None)
            .ConfigureAwait(false);

        Assert.AreEqual("test-game", run.ProfileId);
        Assert.AreEqual(@"C:\Games\TestGame\Game.exe", run.Executable);
        Assert.AreEqual("Game.exe", run.ReceiverProcesses[0]);

        ForwardingHostStatus status = await runtime.GetStatusAsync().ConfigureAwait(false);
        Assert.HasCount(1, status.ClientRuns);
        Assert.AreEqual(run.RunId, status.ClientRuns[0].RunId);

        await runtime.EndRunAsync(run.RunId).ConfigureAwait(false);
    }

    /// <summary>Checks active runs are selected by launched process tree.</summary>
    [TestMethod]
    public async Task ActiveRunUsesRootProcessIdentity()
    {
        Dictionary<string, GameProfile> profiles = CreateProfiles(mouseOutput: MouseOutputKind.None);
        ClientRunStore runs = CreateRunStore(
            profiles,
            () => 201,
            (rootProcessId, processId) => rootProcessId == 200 && processId == 201);

        ClientRunInfo first = await runs
            .StartRunAsync(new ClientRunRequest("test-game", 1, null), CancellationToken.None)
            .ConfigureAwait(false);
        ClientRunInfo second = await runs
            .StartRunAsync(new ClientRunRequest("test-game", 2, null), CancellationToken.None)
            .ConfigureAwait(false);
        await runs.ActivateRunAsync(first.RunId, 100).ConfigureAwait(false);
        await runs.ActivateRunAsync(second.RunId, 200).ConfigureAwait(false);

        _ = await runs.RefreshActiveRunAsync().ConfigureAwait(false);
        IReadOnlyList<ClientRunStatus> statuses = await runs.GetRunStatusAsync().ConfigureAwait(false);

        Assert.IsFalse(statuses[0].IsActive);
        Assert.IsTrue(statuses[1].IsActive);
        Assert.AreEqual(200, statuses[1].RootProcessId);

        await runs.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Checks the active monitor survives one failed foreground poll.</summary>
    [TestMethod]
    public async Task ActiveMonitorContinuesAfterPollingFailure()
    {
        int foregroundCalls = 0;
        ClientRunStore runs = CreateRunStore(
            CreateProfiles(mouseOutput: MouseOutputKind.Viiper),
            () =>
            {
                foregroundCalls++;
                return foregroundCalls == 1
                    ? throw new InvalidOperationException("simulated foreground failure")
                : 42;
            },
            (rootProcessId, processId) => rootProcessId == 10 && processId == 42);
        await using ForwardingHostRuntime runtime = CreateRuntime(
            profiles: null,
            runs);
        ClientRunInfo run = await runtime
            .StartRunAsync(new ClientRunRequest("test-game", 1, null), CancellationToken.None)
            .ConfigureAwait(false);
        await runtime.ActivateRunAsync(run.RunId, 10).ConfigureAwait(false);

        await Task.Delay(TimeSpan.FromMilliseconds(700)).ConfigureAwait(false);

        ForwardingHostStatus status = await runtime.GetStatusAsync().ConfigureAwait(false);
        Assert.AreEqual(1, status.Mouse.EnabledClientCount);
    }

    /// <summary>Checks control connection stop callback.</summary>
    [TestMethod]
    public async Task ControlSessionStopRequestsServerStop()
    {
        await using ForwardingHostRuntime runtime = CreateRuntime();
        bool stopped = false;
        using ForwardingHostControlConnection connection = new(runtime, () => stopped = true, logger: null);

        await connection.StopAsync().ConfigureAwait(false);

        Assert.IsTrue(stopped);
    }

    /// <summary>Checks status through the local pipe control server.</summary>
    [TestMethod]
    public async Task ControlClientGetsStatusFromServer()
    {
        string pipeName = $"Hosting.Tests.{Guid.NewGuid():N}";
        await using ForwardingHostRuntime runtime = CreateRuntime();
        ForwardingHostServer server = new(runtime, pipeName);
        using CancellationTokenSource cancellation = new();
        Task serverTask = server.RunAsync(cancellation.Token);
        ForwardingClient client = new(pipeName, TimeSpan.FromSeconds(2));

        ForwardingHostStatus status = await client.GetStatusAsync().ConfigureAwait(false);

        Assert.AreEqual("mouse", status.Mouse.RouteId);
        await cancellation.CancelAsync().ConfigureAwait(false);
        try
        {
            await serverTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Checks mouse disabling through a connected local client connection.</summary>
    [TestMethod]
    public async Task ControlClientSessionCanDisableAndReuseConnection()
    {
        string pipeName = $"Hosting.Tests.{Guid.NewGuid():N}";
        await using ForwardingHostRuntime runtime = CreateRuntime();
        ForwardingHostServer server = new(runtime, pipeName);
        using CancellationTokenSource cancellation = new();
        Task serverTask = server.RunAsync(cancellation.Token);
        ForwardingClient client = new(pipeName, TimeSpan.FromSeconds(2));

        await using ForwardingClientConnection connection = await client.ConnectAsync().ConfigureAwait(false);
        await connection.EnableMouseAsync().ConfigureAwait(false);
        await connection.DisableMouseAsync().ConfigureAwait(false);

        ForwardingHostStatus status = await client.GetStatusAsync().ConfigureAwait(false);
        Assert.AreEqual(0, status.Mouse.EnabledClientCount);
        Assert.IsFalse(status.Mouse.IsConnected);

        await cancellation.CancelAsync().ConfigureAwait(false);
        try
        {
            await serverTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Checks single-instance locking.</summary>
    [TestMethod]
    public void SingleInstanceRejectsSecondOwner()
    {
        string ownershipName = $@"Local\Hosting.Tests.{Guid.NewGuid():N}";
        using HostSingleInstance? first = HostSingleInstance.TryAcquire(ownershipName);

        Assert.IsNotNull(first);
        Task<bool> secondAcquireTask = Task.Run(() =>
        {
            using HostSingleInstance? second = HostSingleInstance.TryAcquire(ownershipName);
            return second is not null;
        });

        Assert.IsFalse(secondAcquireTask.GetAwaiter().GetResult());
    }

    /// <summary>Checks same-thread reentry rejection.</summary>
    [TestMethod]
    public void SingleInstanceRejectsSecondOwnerInSameThread()
    {
        string ownershipName = $@"Local\Hosting.Tests.{Guid.NewGuid():N}";
        using HostSingleInstance? first = HostSingleInstance.TryAcquire(ownershipName);
        using HostSingleInstance? second = HostSingleInstance.TryAcquire(ownershipName);

        Assert.IsNotNull(first);
        Assert.IsNull(second);
    }

    private static ForwardingHostRuntime CreateRuntime(
        IReadOnlyDictionary<string, GameProfile>? profiles = null,
        ClientRunStore? runs = null)
    {
        ForwardingHostState hostState = new();
        MouseRouteController mouse = new(
            ForwardingRouteIds.Mouse,
            _ => Task.FromResult<IForwardingRoute>(new MouseForwardingRoute(
                new TestMouseInputSource(),
                new TestMouseOutput())),
            logger: null,
            () => hostState.EmulationEnabled);
        ClientRunStore clientRuns = runs ?? new ClientRunStore(
            profiles ?? new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase),
            new ViiperOptions(),
            hostState,
            logger: null);
        return new ForwardingHostRuntime(mouse, clientRuns, hostState);
    }

    private static ClientRunStore CreateRunStore(
        IReadOnlyDictionary<string, GameProfile> profiles,
        Func<int> getForegroundProcessId,
        Func<int, int, bool> isProcessInTree)
    {
        return new ClientRunStore(
            profiles,
            new ViiperOptions(),
            new ForwardingHostState(),
            logger: null,
            getForegroundProcessId,
            isProcessInTree);
    }

    private static Dictionary<string, GameProfile> CreateProfiles(MouseOutputKind mouseOutput)
    {
        return new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["test-game"] = new GameProfile
            {
                Executable = @"C:\Games\TestGame\Game.exe",
                ControllerOutput = ControllerOutputKind.None,
                MouseOutput = mouseOutput,
            },
        };
    }

    private static ForwardingHost CreateHost()
    {
        return new ForwardingHost(new MouseForwardingRoute(
            new TestMouseInputSource(),
            new TestMouseOutput()));
    }

    private sealed class TestMouseInputSource(params MouseInput[] inputs) : IMouseInputSource
    {
        public bool IsConnected => true;

        public Action<int>? BeforeReport { get; set; }

        public void Run(MouseInputHandler handler, CancellationToken cancellationToken = default)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BeforeReport?.Invoke(i);
                MouseInput input = inputs[i];
                handler(in input);
            }
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestMouseOutput : IMouseOutput
    {
        public bool IsConnected => true;

        public List<MouseReport> Reports { get; } = [];

        public bool FilterInput(string? deviceName)
        {
            _ = deviceName;
            return true;
        }

        public ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reports.Add(report);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}

#pragma warning restore CA2007
#pragma warning restore CA2000
#pragma warning restore CA1416
