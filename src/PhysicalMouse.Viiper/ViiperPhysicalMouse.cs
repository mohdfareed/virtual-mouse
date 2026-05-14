using System;
using System.Threading;
using System.Threading.Tasks;
using global::Viiper.Client;
using Microsoft.Extensions.Logging;
using ViiperMouseInput = global::Viiper.Client.Devices.Mouse.MouseInput;

namespace PhysicalMouse.Viiper;

/// <summary>VIIPER transport.</summary>
public sealed partial class ViiperPhysicalMouse : IPhysicalMouse, IDisposable, IAsyncDisposable
{
    private readonly ILogger? _logger;
    private readonly Mutex? _ownershipMutex;
    private readonly ViiperClient? _client;
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
        Mutex ownershipMutex)
    {
        _isConnected = 1;
        _client = client;
        _device = device;
        _logger = logger;
        _ownershipMutex = ownershipMutex;

        BusId = busId;
        DeviceId = deviceId;

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

    /// <inheritdoc />
    public ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken = default)
    {
        ViiperDevice? device = _device;
        if (!IsConnected || device is null)
        {
            throw new InvalidOperationException("Mouse is not connected.");
        }

        // map and forward without extra processing
        return new ValueTask(device.SendAsync(MapReport(report), cancellationToken));
    }

    /// <inheritdoc />
    public bool FilterInput(in MouseInput input)
    {
        return input.DeviceName?.Contains(OwnedDeviceNameFragment, StringComparison.OrdinalIgnoreCase) != true;
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
            if (_client is not null && BusId.HasValue && !string.IsNullOrWhiteSpace(DeviceId))
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
            ReleaseOwnershipMutex(_ownershipMutex);
        }
    }

    // MARK: Internal
    // ========================================================================

    internal static ViiperMouseInput MapReport(MouseReport report)
    {
        // keep the mapping direct and fail on unsupported ranges
        return new ViiperMouseInput
        {
            Buttons = checked((byte)report.Buttons),
            Dx = checked((short)report.DeltaX),
            Dy = checked((short)report.DeltaY),
            Wheel = checked((short)report.WheelDelta),
            Pan = 0,
        };
    }

    private void HookDisconnect(ViiperDevice device)
    {
        Action? onDisconnect = device.OnDisconnect;
        device.OnDisconnect = () =>
        {
            _ = Interlocked.Exchange(ref _isConnected, 0);
            if (_logger is not null && BusId.HasValue && !string.IsNullOrWhiteSpace(DeviceId))
            {
                Log.DisconnectedKnownDevice(_logger, BusId.Value, DeviceId, null);
            }

            onDisconnect?.Invoke();
        };
    }
}
