using System;
using Microsoft.Extensions.Logging;

namespace Outputs.Viiper;

internal static class ViiperOutputLog
{
    private static readonly Action<ILogger, string, uint, Exception?> CreatedDeviceMessage =
        LoggerMessage.Define<string, uint>(
            LogLevel.Information,
            new EventId(1, nameof(CreatedDevice)),
            "Created VIIPER {OutputType} device on bus {BusId}.");

    private static readonly Action<ILogger, string, uint, string, Exception?> RemovedDeviceMessage =
        LoggerMessage.Define<string, uint, string>(
            LogLevel.Information,
            new EventId(2, nameof(RemovedDevice)),
            "Removed VIIPER {OutputType} device {BusId}/{DeviceId}.");

    private static readonly Action<ILogger, string, uint, string, Exception?> ConnectedDeviceMessage =
        LoggerMessage.Define<string, uint, string>(
            LogLevel.Information,
            new EventId(3, nameof(ConnectedDevice)),
            "VIIPER {OutputType} device connected ({BusId}/{DeviceId}).");

    private static readonly Action<ILogger, string, uint, string, Exception?> DisconnectedDeviceMessage =
        LoggerMessage.Define<string, uint, string>(
            LogLevel.Information,
            new EventId(4, nameof(DisconnectedDevice)),
            "VIIPER {OutputType} device disconnected ({BusId}/{DeviceId}).");

    internal static void CreatedDevice(ILogger? logger, string outputType, uint busId)
    {
        if (logger is not null)
        {
            CreatedDeviceMessage(logger, outputType, busId, null);
        }
    }

    internal static void RemovedDevice(ILogger? logger, string outputType, uint busId, string deviceId)
    {
        if (logger is not null)
        {
            RemovedDeviceMessage(logger, outputType, busId, deviceId, null);
        }
    }

    internal static void ConnectedDevice(ILogger? logger, string outputType, uint busId, string deviceId)
    {
        if (logger is not null)
        {
            ConnectedDeviceMessage(logger, outputType, busId, deviceId, null);
        }
    }

    internal static void DisconnectedDevice(ILogger? logger, string outputType, uint busId, string deviceId)
    {
        if (logger is not null)
        {
            DisconnectedDeviceMessage(logger, outputType, busId, deviceId, null);
        }
    }
}
