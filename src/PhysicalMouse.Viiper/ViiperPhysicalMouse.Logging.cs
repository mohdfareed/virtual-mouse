using System;
using Microsoft.Extensions.Logging;

namespace PhysicalMouse.Viiper;

public sealed partial class ViiperPhysicalMouse
{
    // MARK: Logging
    // ========================================================================

    private static class Log
    {
        public static readonly Action<ILogger, uint, string, Exception?> ConnectingKnownDevice =
            LoggerMessage.Define<uint, string>(
                LogLevel.Information,
                new EventId(1, nameof(ConnectingKnownDevice)),
                "Connecting to VIIPER mouse device {BusId}/{DeviceId}.");

        public static readonly Action<ILogger, uint, string, Exception?> ReusingDevice =
            LoggerMessage.Define<uint, string>(
                LogLevel.Information,
                new EventId(2, nameof(ReusingDevice)),
                "Reusing VIIPER mouse device {BusId}/{DeviceId}.");

        public static readonly Action<ILogger, uint, Exception?> CreatingDevice =
            LoggerMessage.Define<uint>(
                LogLevel.Information,
                new EventId(3, nameof(CreatingDevice)),
                "Creating VIIPER mouse device on bus {BusId}.");

        public static readonly Action<ILogger, uint, string, Exception?> RemovedDevice =
            LoggerMessage.Define<uint, string>(
                LogLevel.Information,
                new EventId(4, nameof(RemovedDevice)),
                "Removed VIIPER mouse device {BusId}/{DeviceId}.");

        public static readonly Action<ILogger, uint, string, Exception?> DisconnectedKnownDevice =
            LoggerMessage.Define<uint, string>(
                LogLevel.Information,
                new EventId(5, nameof(DisconnectedKnownDevice)),
                "VIIPER mouse device disconnected ({BusId}/{DeviceId}).");

        public static readonly Action<ILogger, Exception?> DisconnectedDevice =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(6, nameof(DisconnectedDevice)),
                "VIIPER mouse device disconnected.");
    }
}
