using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Inputs.Sdl;
using StreamJsonRpc;

namespace Hosting;

/// <summary>Local forwarding client options.</summary>
public sealed record ForwardingClientOptions
{
    /// <summary>Connection timeout.</summary>
    public TimeSpan? ConnectTimeout { get; init; }
}

/// <summary>Controls a local forwarding server.</summary>
public sealed class ForwardingClient
{
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(2);
    private readonly TimeSpan? _connectTimeout;

    /// <summary>Creates a client with the default host pipe.</summary>
    public ForwardingClient(TimeSpan? connectTimeout = null)
        : this(new ForwardingClientOptions { ConnectTimeout = connectTimeout })
    {
    }

    /// <summary>Creates a client from options.</summary>
    public ForwardingClient(ForwardingClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _connectTimeout = options.ConnectTimeout;
    }

    internal ForwardingClient(string pipeName, TimeSpan? connectTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        PipeName = pipeName;
        _connectTimeout = connectTimeout;
    }

    private string PipeName { get; } = ForwardingServer.PipeName;

    /// <summary>Connects to the host until the returned connection is disposed.</summary>
    public async Task<ForwardingClientConnection> ConnectAsync(CancellationToken cancellationToken = default)
    {
        NamedPipeClientStream pipe = CreatePipe();

        try
        {
            await ConnectAsync(pipe, cancellationToken).ConfigureAwait(false);
            IForwardingHostControl proxy = JsonRpc.Attach<IForwardingHostControl>(pipe);
            return new ForwardingClientConnection(pipe, proxy);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Connects to the host and enables mouse forwarding until the returned connection is disposed.</summary>
    public async Task<ForwardingClientConnection> EnableMouseAsync(CancellationToken cancellationToken = default)
    {
        ForwardingClientConnection connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await connection.EnableMouseAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Gets host status.</summary>
    public async Task<ForwardingHostStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using NamedPipeClientStream pipe = CreatePipe();

        await ConnectAsync(pipe, cancellationToken).ConfigureAwait(false);
        IForwardingHostControl proxy = JsonRpc.Attach<IForwardingHostControl>(pipe);
        using IDisposable proxyHandle = (IDisposable)proxy;
        return await proxy.GetStatusAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Requests the host to stop.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        using NamedPipeClientStream pipe = CreatePipe();

        await ConnectAsync(pipe, cancellationToken).ConfigureAwait(false);
        IForwardingHostControl proxy = JsonRpc.Attach<IForwardingHostControl>(pipe);
        using IDisposable proxyHandle = (IDisposable)proxy;
        await proxy.StopAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sets whether emulation reports are forwarded.</summary>
    public async Task SetEmulationEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        using NamedPipeClientStream pipe = CreatePipe();

        await ConnectAsync(pipe, cancellationToken).ConfigureAwait(false);
        IForwardingHostControl proxy = JsonRpc.Attach<IForwardingHostControl>(pipe);
        using IDisposable proxyHandle = (IDisposable)proxy;
        await proxy.SetEmulationEnabledAsync(enabled).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Toggles whether emulation reports are forwarded.</summary>
    public async Task<bool> ToggleEmulationEnabledAsync(CancellationToken cancellationToken = default)
    {
        using NamedPipeClientStream pipe = CreatePipe();

        await ConnectAsync(pipe, cancellationToken).ConfigureAwait(false);
        IForwardingHostControl proxy = JsonRpc.Attach<IForwardingHostControl>(pipe);
        using IDisposable proxyHandle = (IDisposable)proxy;
        return await proxy.ToggleEmulationEnabledAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sets whether physical motion data is forwarded.</summary>
    public async Task SetPhysicalMotionEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        using NamedPipeClientStream pipe = CreatePipe();

        await ConnectAsync(pipe, cancellationToken).ConfigureAwait(false);
        IForwardingHostControl proxy = JsonRpc.Attach<IForwardingHostControl>(pipe);
        using IDisposable proxyHandle = (IDisposable)proxy;
        await proxy.SetPhysicalMotionEnabledAsync(enabled).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Toggles whether physical motion data is forwarded.</summary>
    public async Task<bool> TogglePhysicalMotionEnabledAsync(CancellationToken cancellationToken = default)
    {
        using NamedPipeClientStream pipe = CreatePipe();

        await ConnectAsync(pipe, cancellationToken).ConfigureAwait(false);
        IForwardingHostControl proxy = JsonRpc.Attach<IForwardingHostControl>(pipe);
        using IDisposable proxyHandle = (IDisposable)proxy;
        return await proxy.TogglePhysicalMotionEnabledAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private NamedPipeClientStream CreatePipe()
    {
        return new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
    }

    private async Task ConnectAsync(NamedPipeClientStream pipe, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_connectTimeout ?? DefaultConnectTimeout);

        try
        {
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out connecting to the host control pipe.");
        }
    }
}

/// <summary>Keeps a host control connection alive.</summary>
public sealed class ForwardingClientConnection : IAsyncDisposable, IDisposable
{
    private NamedPipeClientStream? _pipe;
    private IForwardingHostControl? _proxy;

    internal ForwardingClientConnection(
        NamedPipeClientStream pipe,
        IForwardingHostControl proxy)
    {
        _pipe = pipe;
        _proxy = proxy;
    }

    /// <summary>Enables mouse forwarding on this connection.</summary>
    public Task EnableMouseAsync(CancellationToken cancellationToken = default)
    {
        IForwardingHostControl proxy = GetProxy();
        return proxy.EnableMouseAsync().WaitAsync(cancellationToken);
    }

    /// <summary>Disables mouse forwarding on this connection without disconnecting.</summary>
    public Task DisableMouseAsync(CancellationToken cancellationToken = default)
    {
        IForwardingHostControl proxy = GetProxy();
        return proxy.DisableMouseAsync().WaitAsync(cancellationToken);
    }

    /// <summary>Sets whether emulation reports are forwarded without disconnecting.</summary>
    public Task SetEmulationEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        IForwardingHostControl proxy = GetProxy();
        return proxy.SetEmulationEnabledAsync(enabled).WaitAsync(cancellationToken);
    }

    /// <summary>Toggles whether emulation reports are forwarded without disconnecting.</summary>
    public Task<bool> ToggleEmulationEnabledAsync(CancellationToken cancellationToken = default)
    {
        IForwardingHostControl proxy = GetProxy();
        return proxy.ToggleEmulationEnabledAsync().WaitAsync(cancellationToken);
    }

    /// <summary>Sets whether physical motion data is forwarded without disconnecting.</summary>
    public Task SetPhysicalMotionEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        IForwardingHostControl proxy = GetProxy();
        return proxy.SetPhysicalMotionEnabledAsync(enabled).WaitAsync(cancellationToken);
    }

    /// <summary>Toggles whether physical motion data is forwarded without disconnecting.</summary>
    public Task<bool> TogglePhysicalMotionEnabledAsync(CancellationToken cancellationToken = default)
    {
        IForwardingHostControl proxy = GetProxy();
        return proxy.TogglePhysicalMotionEnabledAsync().WaitAsync(cancellationToken);
    }

    /// <summary>Starts a profile-backed client run.</summary>
    public Task<ClientRunInfo> StartRunAsync(
        ClientRunRequest request,
        CancellationToken cancellationToken = default)
    {
        IForwardingHostControl proxy = GetProxy();
        return proxy.StartRunAsync(request).WaitAsync(cancellationToken);
    }

    /// <summary>Activates a client run after launching the root process.</summary>
    public Task ActivateRunAsync(
        Guid runId,
        int rootProcessId,
        CancellationToken cancellationToken = default)
    {
        IForwardingHostControl proxy = GetProxy();
        return proxy.ActivateRunAsync(runId, rootProcessId).WaitAsync(cancellationToken);
    }

    /// <summary>Attaches a client-visible controller route to a client run.</summary>
    public async Task<ControllerRouteClient> AttachControllerRouteAsync(
        Guid runId,
        SdlControllerInfo controller,
        CancellationToken cancellationToken = default)
    {
        IForwardingHostControl proxy = GetProxy();
        ControllerRouteInfo route = await proxy
            .AttachControllerRouteAsync(runId, controller)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        return await ControllerRouteClient.ConnectAsync(route.Pipe, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Ends a client run.</summary>
    public Task EndRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        IForwardingHostControl proxy = GetProxy();
        return proxy.EndRunAsync(runId).WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        NamedPipeClientStream? pipe = Interlocked.Exchange(ref _pipe, null);
        IForwardingHostControl? proxy = Interlocked.Exchange(ref _proxy, null);
        (proxy as IDisposable)?.Dispose();
        pipe?.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        NamedPipeClientStream? pipe = Interlocked.Exchange(ref _pipe, null);
        IForwardingHostControl? proxy = Interlocked.Exchange(ref _proxy, null);
        (proxy as IDisposable)?.Dispose();

        if (pipe is not null)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    private IForwardingHostControl GetProxy()
    {
        return _proxy ?? throw new ObjectDisposedException(nameof(ForwardingClientConnection));
    }
}
