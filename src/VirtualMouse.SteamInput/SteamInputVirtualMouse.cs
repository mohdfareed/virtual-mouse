using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Steamworks;
using SteamworksInput = Steamworks.SteamInput;

namespace VirtualMouse.SteamInput;

/// <summary>Steam Input source options.</summary>
/// <param name="InitializeSteamApi">Whether this source owns Steam API initialization.</param>
/// <param name="ActionManifestPath">Action manifest path override.</param>
public sealed record SteamInputOptions(bool InitializeSteamApi, string? ActionManifestPath = null)
{
    /// <summary>Default options.</summary>
    public static SteamInputOptions Default { get; } = new(InitializeSteamApi: true);
}

/// <summary>Steam Input mouse source.</summary>
public sealed class SteamInputVirtualMouse : IVirtualMouse, IDisposable
{
    private readonly bool ownsSteamApi;
    private int isConnected = 1;

    // MARK: Construction
    // ========================================================================

    private SteamInputVirtualMouse(SteamInputOptions options)
    {
        ownsSteamApi = options.InitializeSteamApi;
    }

    /// <summary>Creates a Steam Input mouse source.</summary>
    /// <param name="options">Steam Input options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Connected input source.</returns>
    public static ValueTask<SteamInputVirtualMouse> ConnectAsync(
        SteamInputOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options ??= SteamInputOptions.Default;

        if (options.InitializeSteamApi && !SteamAPI.Init())
        {
            throw new InvalidOperationException("Could not initialize Steam API.");
        }

        if (!SteamworksInput.Init(true))
        {
            if (options.InitializeSteamApi)
            {
                SteamAPI.Shutdown();
            }

            throw new InvalidOperationException("Could not initialize Steam Input.");
        }

        if (!string.IsNullOrWhiteSpace(options.ActionManifestPath) &&
            !SteamworksInput.SetInputActionManifestFilePath(Path.GetFullPath(options.ActionManifestPath)))
        {
            _ = SteamworksInput.Shutdown();
            if (options.InitializeSteamApi)
            {
                SteamAPI.Shutdown();
            }

            throw new InvalidOperationException("Could not set Steam Input action manifest path.");
        }

#pragma warning disable CA2000 // Ownership transfers to the caller.
        return ValueTask.FromResult(new SteamInputVirtualMouse(options));
#pragma warning restore CA2000
    }

    // MARK: Implementation
    // ========================================================================

    /// <inheritdoc />
    public bool IsConnected => Volatile.Read(ref isConnected) != 0;

    /// <inheritdoc />
    public void Run(MouseInputHandler handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _ = cancellationToken;
        throw new NotImplementedException("Steam Input polling is not implemented yet.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isConnected, 0) == 0)
        {
            return ValueTask.CompletedTask;
        }

        _ = SteamworksInput.Shutdown();
        if (ownsSteamApi)
        {
            SteamAPI.Shutdown();
        }

        return ValueTask.CompletedTask;
    }
}
