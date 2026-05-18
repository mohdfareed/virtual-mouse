using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SDL3;
using VirtualMouse.Forwarding;

namespace VirtualMouse.Inputs.Sdl;

/// <summary>Connected SDL gamepad source.</summary>
public sealed class SdlGamepadSource : IControllerFeedbackSink, IDisposable, IAsyncDisposable
{
    private const int SensorValueCount = 3;
    private const uint RumbleHoldDurationMilliseconds = uint.MaxValue;

    private readonly float[] _gyroData = new float[SensorValueCount];
    private readonly float[] _accelerometerData = new float[SensorValueCount];
    private readonly SdlGamepadRuntime.Lease _runtimeLease;
    private nint _gamepad;
    private int _isConnected = 1;
    private int _motionEnabled = 1;

    private SdlGamepadSource(
        nint gamepad,
        SdlControllerInfo controller,
        SdlGamepadRuntime.Lease runtimeLease)
    {
        _gamepad = gamepad;
        Controller = controller;
        _runtimeLease = runtimeLease;
        HasGyro = EnableSensor(gamepad, SDL.SensorType.Gyro);
        HasAccelerometer = EnableSensor(gamepad, SDL.SensorType.Accel);
    }

    /// <summary>Gets whether the controller is connected.</summary>
    public bool IsConnected => Volatile.Read(ref _isConnected) != 0;

    /// <summary>Gets the connected SDL controller.</summary>
    public SdlControllerInfo Controller { get; }

    /// <summary>Gets whether the connected controller exposes a gyro sensor.</summary>
    public bool HasGyro { get; }

    /// <summary>Gets whether the connected controller exposes an accelerometer sensor.</summary>
    public bool HasAccelerometer { get; }

    /// <summary>Gets controller feature groups supported by this source.</summary>
    public ControllerFeatures Features =>
        ControllerFeatures.StandardControls |
        ControllerFeatures.Rumble |
        (HasGyro || HasAccelerometer ? ControllerFeatures.Motion : ControllerFeatures.None);

    /// <summary>Gets or sets whether motion data is emitted.</summary>
    public bool MotionEnabled
    {
        get => Volatile.Read(ref _motionEnabled) != 0;
        set => Volatile.Write(ref _motionEnabled, value ? 1 : 0);
    }

    /// <summary>Connects to one SDL controller.</summary>
    public static SdlGamepadSource Connect(SdlControllerInfo controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        SdlGamepadRuntime.Lease? lease = null;
        try
        {
            lease = SdlGamepadRuntime.Acquire();
            nint gamepad = SdlControllerCatalog.OpenGamepad(controller);
            if (gamepad == 0)
            {
                throw new InvalidOperationException($"Could not open SDL controller: {SDL.GetError()}");
            }

            SdlGamepadSource source = new(gamepad, controller, lease);
            lease = null;
            return source;
        }
        finally
        {
            lease?.Dispose();
        }
    }

    /// <inheritdoc />
    public bool TrySendFeedback(ControllerFeedback feedback)
    {
        if (feedback.Rumble is not { } rumble)
        {
            return false;
        }

        nint gamepad = _gamepad;
        return IsConnected &&
            gamepad != 0 &&
            SDL.RumbleGamepad(
                gamepad,
                rumble.LowFrequency,
                rumble.HighFrequency,
                rumble.LowFrequency == 0 && rumble.HighFrequency == 0 ? 0 : RumbleHoldDurationMilliseconds);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _ = Interlocked.Exchange(ref _isConnected, 0);
        nint gamepad = Interlocked.Exchange(ref _gamepad, 0);
        if (gamepad != 0)
        {
            _ = SDL.RumbleGamepad(gamepad, 0, 0, 0);
            SDL.CloseGamepad(gamepad);
        }

        _runtimeLease.Dispose();
        return ValueTask.CompletedTask;
    }

    internal bool ProcessEvent(SDL.Event sdlEvent)
    {
        SDL.EventType eventType = (SDL.EventType)sdlEvent.Type;
        if (eventType == SDL.EventType.GamepadUpdateComplete &&
            sdlEvent.GDevice.Which == Controller.InstanceId)
        {
            return true;
        }

        if (eventType == SDL.EventType.GamepadSensorUpdate &&
            sdlEvent.GSensor.Which == Controller.InstanceId)
        {
            UpdateMotion(sdlEvent.GSensor);
            return true;
        }

        if (eventType == SDL.EventType.GamepadRemoved &&
            sdlEvent.GDevice.Which == Controller.InstanceId)
        {
            _ = Interlocked.Exchange(ref _isConnected, 0);
            throw new SdlGamepadDisconnectedException($"SDL controller \"{Controller.Name}\" was disconnected.");
        }

        return false;
    }

    internal ControllerState ReadCurrentState()
    {
        nint gamepad = _gamepad;
        bool motionEnabled = MotionEnabled;
        return SdlGamepadStateReader.ReadState(
            gamepad,
            motionEnabled && HasGyro,
            motionEnabled && HasAccelerometer,
            _gyroData,
            _accelerometerData);
    }

    private unsafe void UpdateMotion(SDL.GamepadSensorEvent sensorEvent)
    {
        ReadOnlySpan<float> data = new(sensorEvent.Data, SensorValueCount);
        SDL.SensorType sensor = (SDL.SensorType)sensorEvent.Sensor;
        if (sensor == SDL.SensorType.Gyro)
        {
            data.CopyTo(_gyroData);
        }
        else if (sensor == SDL.SensorType.Accel)
        {
            data.CopyTo(_accelerometerData);
        }
    }

    private static bool EnableSensor(nint gamepad, SDL.SensorType sensor)
    {
        return gamepad != 0 &&
            SDL.GamepadHasSensor(gamepad, sensor) &&
            (SDL.GamepadSensorEnabled(gamepad, sensor) ||
            SDL.SetGamepadSensorEnabled(gamepad, sensor, enabled: true));
    }
}

/// <summary>Thrown when an SDL controller disconnects while streaming.</summary>
public sealed class SdlGamepadDisconnectedException : InvalidOperationException
{
    /// <summary>Creates a disconnect exception.</summary>
    public SdlGamepadDisconnectedException()
    {
    }

    /// <summary>Creates a disconnect exception.</summary>
    public SdlGamepadDisconnectedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a disconnect exception.</summary>
    public SdlGamepadDisconnectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static class SdlGamepadRuntime
{
    private static readonly Lock Gate = new();
    private static int _leaseCount;

    public static Lease Acquire()
    {
        lock (Gate)
        {
            if (_leaseCount == 0 && !SDL.Init(SDL.InitFlags.Gamepad | SDL.InitFlags.Events | SDL.InitFlags.Sensor))
            {
                throw new InvalidOperationException($"Could not initialize SDL: {SDL.GetError()}");
            }

            _leaseCount++;
            return new Lease();
        }
    }

    public sealed class Lease : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            lock (Gate)
            {
                _leaseCount--;
                if (_leaseCount == 0)
                {
                    SDL.QuitSubSystem(SDL.InitFlags.Gamepad | SDL.InitFlags.Events | SDL.InitFlags.Sensor);
                }
            }
        }
    }
}

/// <summary>Runs one SDL event loop for a group of controller sources.</summary>
public static class SdlGamepadEventLoop
{
    /// <summary>Runs the sources until cancellation.</summary>
    public static void Run(
        IReadOnlyList<SdlGamepadSource> sources,
        Action<SdlGamepadSource, ControllerState> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(handler);

        while (!cancellationToken.IsCancellationRequested)
        {
            while (SDL.PollEvent(out SDL.Event sdlEvent))
            {
                foreach (SdlGamepadSource source in sources)
                {
                    if (source.ProcessEvent(sdlEvent))
                    {
                        handler(source, source.ReadCurrentState());
                        break;
                    }
                }
            }

            SDL.Delay(1);
        }
    }
}
