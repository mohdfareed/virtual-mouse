using System;
using System.Threading;
using System.Threading.Tasks;
using Viiper.Client;
using Viiper.Client.Devices.Mouse;
using Viiper.Client.Types;

namespace PhysicalMouse.Viiper;

public sealed partial class ViiperPhysicalMouse
{
    private static void ValidateOptions(ViiperOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.DeviceId) && !options.BusId.HasValue)
        {
            throw new ArgumentException("DeviceId requires BusId.", nameof(options));
        }
    }

    private static async Task<uint> ResolveBusIdAsync(
        ViiperClient client,
        uint? busId,
        CancellationToken cancellationToken)
    {
        if (busId.HasValue)
        {
            return busId.Value;
        }

        BusListResponse buses = await client.BusListAsync(cancellationToken).ConfigureAwait(false);
        if (buses.Buses.Length > 0)
        {
            return buses.Buses[0];
        }

        BusCreateResponse created = await client.BusCreateAsync(null, cancellationToken).ConfigureAwait(false);
        return created.BusID;
    }

    private static async Task<Device?> FindReusableDeviceAsync(
        ViiperClient client,
        uint busId,
        CancellationToken cancellationToken)
    {
        DevicesListResponse devices = await client.BusDevicesListAsync(busId, cancellationToken).ConfigureAwait(false);
        return SelectReusableDevice(devices.Devices);
    }

    internal static MouseInput MapReport(MouseReport report)
    {
        // keep the mapping direct and fail on unsupported ranges
        return new MouseInput
        {
            Buttons = checked((byte)report.Buttons),
            Dx = checked((short)report.DeltaX),
            Dy = checked((short)report.DeltaY),
            Wheel = checked((short)report.WheelDelta),
            Pan = 0,
        };
    }

    internal static Device? SelectReusableDevice(Device[] devices)
    {
        Device[] mouseDevices = Array.FindAll(devices, static device => string.Equals(device.Type, "mouse", StringComparison.Ordinal));
        return mouseDevices.Length == 1 ? mouseDevices[0] : null;
    }
}
