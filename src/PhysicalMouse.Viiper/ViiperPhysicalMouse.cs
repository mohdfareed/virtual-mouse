using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Viiper.Client;
using Viiper.Client.Types;

namespace PhysicalMouse.Viiper;

/// <summary>VIIPER transport.</summary>
public sealed partial class ViiperPhysicalMouse : IPhysicalMouse, IDisposable, IAsyncDisposable
{
    private readonly ViiperClient? _client;
    private readonly ILogger? _logger;
    private readonly bool _removeCreatedDeviceOnDispose;
    private ViiperDevice? _device;
    private int _isConnected;

    // MARK: Construction
    // ========================================================================

    /// <summary>Wraps an existing VIIPER device.</summary>
    /// <param name="device">Connected device stream.</param>
    /// <param name="logger">Logger for lifecycle events.</param>
    public ViiperPhysicalMouse(ViiperDevice device, ILogger? logger = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _logger = logger;
        _isConnected = 1;
        HookDisconnect(device);
    }

    private ViiperPhysicalMouse(
        ViiperClient client,
        ViiperDevice device,
        uint busId,
        string deviceId,
        ILogger? logger,
        bool removeCreatedDeviceOnDispose)
    {
        _client = client;
        _device = device;
        _logger = logger;
        BusId = busId;
        DeviceId = deviceId;
        _removeCreatedDeviceOnDispose = removeCreatedDeviceOnDispose;
        _isConnected = 1;
        HookDisconnect(device);
    }

    // MARK: Implementation
    // ========================================================================

    /// <inheritdoc />
    public bool IsConnected => Volatile.Read(ref _isConnected) != 0;

    /// <summary>Gets the connected bus ID, if known.</summary>
    public uint? BusId { get; }

    /// <summary>Gets the connected device ID, if known.</summary>
    public string? DeviceId { get; }

    /// <summary>
    /// Creates and connects a VIIPER mouse device.
    /// </summary>
    /// <param name="options">Connection options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Connected transport.</returns>
    public static async Task<ViiperPhysicalMouse> ConnectAsync(
        ViiperOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        ViiperClient client = new(options.Host, options.Port, options.Password);

        try
        {
            // reconnect to a known device first when sticky IDs are available
            if (options.BusId.HasValue && !string.IsNullOrWhiteSpace(options.DeviceId))
            {
                if (options.Logger is not null)
                {
                    Log.ConnectingKnownDevice(options.Logger, options.BusId.Value, options.DeviceId, null);
                }

                ViiperDevice device = await client.ConnectDeviceAsync(
                    options.BusId.Value,
                    options.DeviceId,
                    cancellationToken).ConfigureAwait(false);

                return new ViiperPhysicalMouse(
                    client,
                    device,
                    options.BusId.Value,
                    options.DeviceId,
                    options.Logger,
                    removeCreatedDeviceOnDispose: false);
            }

            // find a reusable device or create one when needed
            uint resolvedBusId = await ResolveBusIdAsync(client, options.BusId, cancellationToken).ConfigureAwait(false);
            Device? existingDevice = await FindReusableDeviceAsync(client, resolvedBusId, cancellationToken).ConfigureAwait(false);
            if (existingDevice is not null)
            {
                if (options.Logger is not null)
                {
                    Log.ReusingDevice(options.Logger, existingDevice.BusID, existingDevice.DevId, null);
                }

                ViiperDevice device = await client.ConnectDeviceAsync(
                    existingDevice.BusID,
                    existingDevice.DevId,
                    cancellationToken).ConfigureAwait(false);

                return new ViiperPhysicalMouse(
                    client,
                    device,
                    existingDevice.BusID,
                    existingDevice.DevId,
                    options.Logger,
                    removeCreatedDeviceOnDispose: false);
            }

            if (options.Logger is not null)
            {
                Log.CreatingDevice(options.Logger, resolvedBusId, null);
            }

            Device createdDevice = await client.BusDeviceAddAsync(
                resolvedBusId,
                new DeviceCreateRequest
                {
                    Type = "mouse",
                },
                cancellationToken).ConfigureAwait(false);

            try
            {
                ViiperDevice device = await client.ConnectDeviceAsync(
                    createdDevice.BusID,
                    createdDevice.DevId,
                    cancellationToken).ConfigureAwait(false);

                return new ViiperPhysicalMouse(
                    client,
                    device,
                    createdDevice.BusID,
                    createdDevice.DevId,
                    options.Logger,
                    options.RemoveCreatedDeviceOnDispose);
            }
            catch
            {
                _ = await client.BusDeviceRemoveAsync(createdDevice.BusID, createdDevice.DevId, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _device is null)
        {
            throw new InvalidOperationException("Mouse is not connected.");
        }

        // map and forward without extra processing
        await _device.SendAsync(MapReport(report), cancellationToken).ConfigureAwait(false);
    }

    // MARK: Disposal
    // ========================================================================

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // disconnect once and release the owned VIIPER objects
        _ = Interlocked.Exchange(ref _isConnected, 0);
        ViiperDevice? device = Interlocked.Exchange(ref _device, null);
        if (device is null)
        {
            return;
        }

        try
        {
            if (_removeCreatedDeviceOnDispose && _client is not null && BusId.HasValue && !string.IsNullOrWhiteSpace(DeviceId))
            {
                _ = await _client.BusDeviceRemoveAsync(BusId.Value, DeviceId, CancellationToken.None).ConfigureAwait(false);
                if (_logger is not null)
                {
                    Log.RemovedDevice(_logger, BusId.Value, DeviceId, null);
                }
            }
        }
        finally
        {
            await device.DisposeAsync().ConfigureAwait(false);
            _client?.Dispose();
        }
    }

    // MARK: Helpers
    // ========================================================================

    private void HookDisconnect(ViiperDevice device)
    {
        Action? onDisconnect = device.OnDisconnect;
        device.OnDisconnect = () =>
        {
            _ = Interlocked.Exchange(ref _isConnected, 0);
            if (_logger is not null)
            {
                if (BusId.HasValue && !string.IsNullOrWhiteSpace(DeviceId))
                {
                    Log.DisconnectedKnownDevice(_logger, BusId.Value, DeviceId, null);
                }
                else
                {
                    Log.DisconnectedDevice(_logger, null);
                }
            }

            onDisconnect?.Invoke();
        };
    }
}
