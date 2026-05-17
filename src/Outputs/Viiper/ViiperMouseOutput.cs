using System;
using System.Threading;
using System.Threading.Tasks;
using global::Viiper.Client;
using global::Viiper.Client.Types;
using Inputs;
using Microsoft.Extensions.Logging;
using ViiperMouseInput = global::Viiper.Client.Devices.Mouse.MouseInput;

namespace Outputs.Viiper;

/// <summary>VIIPER mouse output.</summary>
public sealed class ViiperMouseOutput : IMouseOutput, IDisposable, IAsyncDisposable
{
    internal const ushort OwnedVendorId = 0x6969;
    internal const ushort OwnedProductId = 0x5050;
    internal const string OwnershipMutexName = @"Local\Outputs.Viiper.Mouse";

    private static readonly ViiperOutputDeviceDefinition DeviceDefinition = new(
        "mouse",
        OwnedVendorId,
        OwnedProductId,
        OwnershipMutexName,
        "mouse");

    private readonly ViiperOutputDevice _device;

    // MARK: Construction
    // ========================================================================

    /// <summary>Wraps an existing VIIPER device.</summary>
    /// <param name="device">Connected device stream.</param>
    /// <param name="logger">Logger for lifecycle events.</param>
    public ViiperMouseOutput(ViiperDevice device, ILogger? logger = null)
    {
        _ = logger;
        _device = new ViiperOutputDevice(device);
    }

    private ViiperMouseOutput(ViiperOutputDevice device)
    {
        _device = device;
    }

    // MARK: Properties
    // ========================================================================

    /// <inheritdoc />
    public bool IsConnected => _device.IsConnected;

    /// <summary>Gets the connected bus ID, if known.</summary>
    public uint? BusId => _device.BusId;

    /// <summary>Gets the connected device ID, if known.</summary>
    public string? DeviceId => _device.DeviceId;

    // MARK: Connection
    // ========================================================================

    /// <summary>Creates and connects a VIIPER mouse device.</summary>
    /// <param name="options">Connection options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task<ViiperMouseOutput> ConnectAsync(
        ViiperOptions options,
        CancellationToken cancellationToken = default)
    {
        return ViiperOutputConnector.ConnectExclusiveAsync(
            options,
            DeviceDefinition,
            "Another VIIPER mouse output connection is already active.",
            device => new ViiperMouseOutput(device),
            cancellationToken);
    }

    // MARK: Output
    // ========================================================================

    /// <inheritdoc />
    public ValueTask SendAsync(MouseReport report, CancellationToken cancellationToken = default)
    {
        return new ValueTask(_device
            .GetDeviceOrThrow("Mouse is not connected.")
            .SendAsync(MapReport(report), cancellationToken));
    }

    /// <summary>Returns whether the input should be forwarded to this transport.</summary>
    /// <param name="deviceName">Source device name, when known.</param>
    /// <remarks>This filter prevents loopback input issues.</remarks>
    public bool FilterInput(string? deviceName)
    {
        return !IsOwnedDeviceName(deviceName);
    }

    // MARK: Disposal
    // ========================================================================

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return _device.DisposeAsync();
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

    internal static bool IsOwnedDevice(Device device)
    {
        return DeviceDefinition.IsOwnedDevice(device);
    }

    internal static bool IsOwnedDeviceName(string? deviceName)
    {
        return DeviceDefinition.IsOwnedDeviceName(deviceName);
    }

    internal static string FormatUsbId(ushort value)
    {
        return ViiperOutputDeviceDefinition.FormatUsbId(value);
    }

    internal static ViiperOutputOwnership? TryAcquireOwnership(string name)
    {
        return ViiperOutputOwnership.TryAcquire(name);
    }
}
