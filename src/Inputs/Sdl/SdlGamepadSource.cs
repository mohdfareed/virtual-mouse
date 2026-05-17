using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SDL3;

namespace Inputs.Sdl;

/// <summary>SDL gamepad connection options.</summary>
public sealed record SdlGamepadOptions
{
    /// <summary>Zero-based SDL gamepad index for buttons and axes.</summary>
    public int DeviceIndex { get; init; }

    /// <summary>SDL input mode.</summary>
    public SdlGamepadInputMode Mode { get; init; } = SdlGamepadInputMode.Physical;

    /// <summary>Use a physical SDL gamepad for motion and rumble while the primary input mode is Steam.</summary>
    public bool UsePhysicalMotion { get; init; }

    /// <summary>Zero-based SDL gamepad index for physical motion and rumble.</summary>
    public int? MotionDeviceIndex { get; init; }
}

/// <summary>SDL gamepad input mode.</summary>
public enum SdlGamepadInputMode
{
    /// <summary>Use a gamepad reported directly by SDL without a Steam handle.</summary>
    Physical,

    /// <summary>Use a gamepad reported by Steam Input through SDL.</summary>
    Steam,
}

/// <summary>SDL gamepad discovered on the system.</summary>
/// <param name="Index">Zero-based gamepad index.</param>
/// <param name="InstanceId">SDL joystick instance id.</param>
/// <param name="Name">Device name.</param>
/// <param name="SteamHandle">SDL Steam handle; zero means not Steam-routed.</param>
/// <param name="VendorId">USB vendor id when known.</param>
/// <param name="ProductId">USB product id when known.</param>
/// <param name="Path">SDL device path when known.</param>
public sealed record SdlGamepadInfo(
    int Index,
    uint InstanceId,
    string Name,
    ulong SteamHandle,
    ushort VendorId,
    ushort ProductId,
    string? Path)
{
    /// <summary>Gets whether SDL reports this gamepad through Steam Input.</summary>
    public bool IsSteamInput => SteamHandle != 0;

    /// <summary>Gets whether SDL reports a gyro sensor.</summary>
    public bool HasGyro { get; init; }

    /// <summary>Gets whether SDL reports an accelerometer sensor.</summary>
    public bool HasAccelerometer { get; init; }
}

/// <summary>SDL gamepad input source.</summary>
public sealed class SdlGamepadSource : IGamepadInputSource, IGamepadRumbleSink, IDisposable
{
    private const int EventWaitTimeoutMilliseconds = 50;
    private const int SensorValueCount = 3;
    private const uint RumbleHoldDurationMilliseconds = uint.MaxValue;

    private readonly float[] _gyroData = new float[SensorValueCount];
    private readonly float[] _accelerometerData = new float[SensorValueCount];
    private nint _gamepad;
    private nint _motionGamepad;
    private int _isConnected = 1;

    private SdlGamepadSource(
        nint gamepad,
        uint instanceId,
        string deviceName,
        SdlGamepadInputMode mode,
        bool usePhysicalMotion,
        ulong steamHandle,
        ushort vendorId,
        ushort productId,
        string? path,
        nint motionGamepad,
        SdlGamepadInfo? motionInfo,
        bool hasGyro,
        bool hasAccelerometer)
    {
        _gamepad = gamepad;
        _motionGamepad = motionGamepad;
        InstanceId = instanceId;
        DeviceName = deviceName;
        Mode = mode;
        UsesPhysicalMotion = usePhysicalMotion;
        SteamHandle = steamHandle;
        VendorId = vendorId;
        ProductId = productId;
        Path = path;
        MotionDeviceName = motionInfo?.Name;
        MotionInstanceId = motionInfo?.InstanceId;
        MotionVendorId = motionInfo?.VendorId;
        MotionProductId = motionInfo?.ProductId;
        MotionPath = motionInfo?.Path;
        HasGyro = hasGyro;
        HasAccelerometer = hasAccelerometer;
    }

    /// <inheritdoc />
    public bool IsConnected => Volatile.Read(ref _isConnected) != 0;

    /// <summary>Gets the SDL joystick instance id.</summary>
    public uint InstanceId { get; }

    /// <summary>Gets the SDL device name.</summary>
    public string DeviceName { get; }

    /// <summary>Gets the selected SDL input mode.</summary>
    public SdlGamepadInputMode Mode { get; }

    /// <summary>Gets whether motion and rumble use a physical SDL gamepad.</summary>
    public bool UsesPhysicalMotion { get; }

    /// <summary>Gets the SDL Steam handle; zero means not Steam-routed.</summary>
    public ulong SteamHandle { get; }

    /// <summary>Gets whether SDL reports this gamepad through Steam Input.</summary>
    public bool IsSteamInput => SteamHandle != 0;

    /// <summary>Gets the USB vendor id when known.</summary>
    public ushort VendorId { get; }

    /// <summary>Gets the USB product id when known.</summary>
    public ushort ProductId { get; }

    /// <summary>Gets the SDL device path when known.</summary>
    public string? Path { get; }

    /// <summary>Gets the SDL motion device name in mixed mode.</summary>
    public string? MotionDeviceName { get; }

    /// <summary>Gets the SDL motion device instance id in mixed mode.</summary>
    public uint? MotionInstanceId { get; }

    /// <summary>Gets the motion device USB vendor id when known.</summary>
    public ushort? MotionVendorId { get; }

    /// <summary>Gets the motion device USB product id when known.</summary>
    public ushort? MotionProductId { get; }

    /// <summary>Gets the motion device SDL path when known.</summary>
    public string? MotionPath { get; }

    /// <summary>Gets whether the connected gamepad exposes a gyro sensor.</summary>
    public bool HasGyro { get; }

    /// <summary>Gets whether the connected gamepad exposes an accelerometer sensor.</summary>
    public bool HasAccelerometer { get; }

    /// <summary>Lists SDL gamepads.</summary>
    public static IReadOnlyList<SdlGamepadInfo> GetGamepads()
    {
        try
        {
            return GetGamepadsCore();
        }
        catch (DllNotFoundException exception)
        {
            throw CreateSdlUnavailableException(exception);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw CreateSdlUnavailableException(exception);
        }
    }

    /// <summary>Creates an SDL gamepad source.</summary>
    /// <param name="options">Connection options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task<SdlGamepadSource> ConnectAsync(
        SdlGamepadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new SdlGamepadOptions();
        ValidateOptions(options);

        try
        {
#pragma warning disable CA2000 // Ownership transfers to the caller.
            return Task.FromResult(Connect(options));
#pragma warning restore CA2000
        }
        catch (DllNotFoundException exception)
        {
            throw CreateSdlUnavailableException(exception);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw CreateSdlUnavailableException(exception);
        }
    }

    /// <inheritdoc />
    public void Run(GamepadInputHandler handler, CancellationToken cancellationToken = default)
    {
        Run(handler, timingHandler: null, cancellationToken);
    }

    internal void Run(
        GamepadInputHandler handler,
        Action<long, long>? timingHandler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!IsConnected || _gamepad == 0)
        {
            throw new InvalidOperationException("SDL gamepad source is not connected.");
        }

        bool hasPreviousState = false;
        GamepadState previousState = default;

        EmitCurrentState(Stopwatch.GetTimestamp());
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!WaitForSdlEvent(out SDL.Event sdlEvent, cancellationToken))
            {
                continue;
            }

            ProcessEvent(sdlEvent);
            while (SDL.PollEvent(out sdlEvent))
            {
                ProcessEvent(sdlEvent);
            }
        }

        void ProcessEvent(SDL.Event sdlEvent)
        {
            SDL.EventType eventType = (SDL.EventType)sdlEvent.Type;
            if (eventType == SDL.EventType.GamepadUpdateComplete &&
                sdlEvent.GDevice.Which == InstanceId)
            {
                EmitCurrentState(Stopwatch.GetTimestamp());
            }
            else if (eventType == SDL.EventType.GamepadSensorUpdate &&
                IsMotionEvent(sdlEvent.GSensor.Which, InstanceId, MotionInstanceId))
            {
                UpdateMotion(sdlEvent.GSensor);
                EmitCurrentState(Stopwatch.GetTimestamp());
            }
            else if (eventType == SDL.EventType.GamepadRemoved &&
                sdlEvent.GDevice.Which == InstanceId)
            {
                _ = Interlocked.Exchange(ref _isConnected, 0);
                throw new InvalidOperationException($"SDL gamepad \"{DeviceName}\" was disconnected.");
            }
            else if (eventType == SDL.EventType.GamepadRemoved &&
                MotionInstanceId.HasValue &&
                sdlEvent.GDevice.Which == MotionInstanceId.Value)
            {
                _ = Interlocked.Exchange(ref _isConnected, 0);
                throw new InvalidOperationException($"SDL motion gamepad \"{MotionDeviceName}\" was disconnected.");
            }
        }

        void EmitCurrentState(long startedTimestamp)
        {
            GamepadState state = ReadState(_gamepad, HasGyro, HasAccelerometer, _gyroData, _accelerometerData);

            if (hasPreviousState && state == previousState)
            {
                return;
            }

            long emittedTimestamp = Stopwatch.GetTimestamp();
            timingHandler?.Invoke(startedTimestamp, emittedTimestamp);
            GamepadInput input = new(state, DeviceName);
            handler(in input);
            previousState = state;
            hasPreviousState = true;
        }
    }

    /// <inheritdoc />
    public bool TryRumble(GamepadRumble rumble)
    {
        nint gamepad = GetRumbleGamepad();
        return IsConnected &&
            gamepad != 0 &&
            SDL.RumbleGamepad(
                gamepad,
                rumble.LowFrequency,
                rumble.HighFrequency,
                rumble.IsEmpty ? 0 : RumbleHoldDurationMilliseconds);
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
        nint motionGamepad = Interlocked.Exchange(ref _motionGamepad, 0);
        if (gamepad != 0)
        {
            if (motionGamepad == 0)
            {
                _ = SDL.RumbleGamepad(gamepad, 0, 0, 0);
            }

            SDL.CloseGamepad(gamepad);
        }

        if (motionGamepad != 0)
        {
            _ = SDL.RumbleGamepad(motionGamepad, 0, 0, 0);
            SDL.CloseGamepad(motionGamepad);
        }

        if (gamepad != 0 || motionGamepad != 0)
        {
            SDL.QuitSubSystem(SDL.InitFlags.Gamepad);
        }

        return ValueTask.CompletedTask;
    }

    internal static GamepadState ReadState(nint gamepad)
    {
        return ReadState(
            gamepad,
            hasGyro: false,
            hasAccelerometer: false,
            [],
            []);
    }

    internal static bool IsMotionEvent(uint instanceId, uint primaryInstanceId, uint? motionInstanceId)
    {
        return motionInstanceId.HasValue
            ? instanceId == motionInstanceId.Value
            : instanceId == primaryInstanceId;
    }

    internal static GamepadMotion CreateMotion(
        bool hasGyro,
        ReadOnlySpan<float> gyro,
        bool hasAccelerometer,
        ReadOnlySpan<float> accelerometer)
    {
        return new GamepadMotion(
            hasGyro,
            hasGyro ? gyro[0] : 0,
            hasGyro ? gyro[1] : 0,
            hasGyro ? gyro[2] : 0,
            hasAccelerometer,
            hasAccelerometer ? accelerometer[0] : 0,
            hasAccelerometer ? accelerometer[1] : 0,
            hasAccelerometer ? accelerometer[2] : 0);
    }

    private static GamepadState ReadState(
        nint gamepad,
        bool hasGyro,
        bool hasAccelerometer,
        ReadOnlySpan<float> gyro,
        ReadOnlySpan<float> accelerometer)
    {
        GamepadButtons buttons = GamepadButtons.None;
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.South, GamepadButtons.South);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.East, GamepadButtons.East);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.West, GamepadButtons.West);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.North, GamepadButtons.North);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.Back, GamepadButtons.Back);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.Guide, GamepadButtons.Guide);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.Start, GamepadButtons.Start);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.LeftStick, GamepadButtons.LeftStick);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.RightStick, GamepadButtons.RightStick);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.LeftShoulder, GamepadButtons.LeftShoulder);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.RightShoulder, GamepadButtons.RightShoulder);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.DPadUp, GamepadButtons.DPadUp);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.DPadDown, GamepadButtons.DPadDown);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.DPadLeft, GamepadButtons.DPadLeft);
        buttons = Apply(buttons, gamepad, SDL.GamepadButton.DPadRight, GamepadButtons.DPadRight);

        return new GamepadState(
            buttons,
            SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.LeftX),
            SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.LeftY),
            SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.RightX),
            SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.RightY),
            ToTrigger(SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.LeftTrigger)),
            ToTrigger(SDL.GetGamepadAxis(gamepad, SDL.GamepadAxis.RightTrigger)),
            CreateMotion(hasGyro, gyro, hasAccelerometer, accelerometer));
    }

    internal static ushort ToTrigger(short value)
    {
        return value <= 0 ? (ushort)0 : (ushort)value;
    }

    private static SdlGamepadSource Connect(SdlGamepadOptions options)
    {
        bool initialized = false;
        nint gamepad = 0;
        nint motionGamepad = 0;

        try
        {
            InitializeGamepads();
            initialized = true;

            uint[] gamepadIds = SDL.GetGamepads(out int count) ?? [];
            if (count <= 0)
            {
                throw new InvalidOperationException("No SDL gamepads were found.");
            }

            gamepad = OpenGamepad(gamepadIds, count, options.DeviceIndex);
            uint instanceId = gamepadIds[options.DeviceIndex];
            if (gamepad == 0)
            {
                throw new InvalidOperationException($"Could not open SDL gamepad: {SDL.GetError()}");
            }

            SdlGamepadInfo info = CreateGamepadInfo(options.DeviceIndex, instanceId, gamepad);
            EnsureInputMode(info, options.Mode);

            SdlGamepadInfo? motionInfo = null;
            nint motionHandle = gamepad;
            if (options.UsePhysicalMotion)
            {
                int motionDeviceIndex = ResolveMotionDeviceIndex(GetGamepadInfos(gamepadIds, count), info, options);
                motionGamepad = OpenGamepad(gamepadIds, count, motionDeviceIndex);
                uint motionInstanceId = gamepadIds[motionDeviceIndex];
                if (motionGamepad == 0)
                {
                    throw new InvalidOperationException($"Could not open SDL motion gamepad: {SDL.GetError()}");
                }

                motionInfo = CreateGamepadInfo(motionDeviceIndex, motionInstanceId, motionGamepad);
                EnsurePhysicalGamepad(motionInfo);
                motionHandle = motionGamepad;
            }

            bool hasGyro = EnableSensor(motionHandle, SDL.SensorType.Gyro);
            bool hasAccelerometer = EnableSensor(motionHandle, SDL.SensorType.Accel);

            nint connectedGamepad = gamepad;
            nint connectedMotionGamepad = motionGamepad;
            gamepad = 0;
            motionGamepad = 0;
            initialized = false;
            return new SdlGamepadSource(
                connectedGamepad,
                instanceId,
                info.Name,
                options.Mode,
                options.UsePhysicalMotion,
                info.SteamHandle,
                info.VendorId,
                info.ProductId,
                info.Path,
                connectedMotionGamepad,
                motionInfo,
                hasGyro,
                hasAccelerometer);
        }
        finally
        {
            if (motionGamepad != 0)
            {
                SDL.CloseGamepad(motionGamepad);
            }

            if (gamepad != 0)
            {
                SDL.CloseGamepad(gamepad);
            }

            if (initialized)
            {
                SDL.QuitSubSystem(SDL.InitFlags.Gamepad);
            }
        }
    }

    private static List<SdlGamepadInfo> GetGamepadsCore()
    {
        InitializeGamepads();
        uint[] gamepadIds = SDL.GetGamepads(out int count) ?? [];
        try
        {
            if (gamepadIds.Length == 0 && count > 0)
            {
                throw new InvalidOperationException($"Could not list SDL gamepads: {SDL.GetError()}");
            }

            List<SdlGamepadInfo> gamepads = new(count);
            for (int i = 0; i < count; i++)
            {
                uint instanceId = gamepadIds[i];
                nint gamepad = SDL.OpenGamepad(instanceId);

                try
                {
                    gamepads.Add(gamepad == 0
                        ? CreateGamepadInfoForId(gamepads.Count, instanceId)
                        : CreateGamepadInfo(gamepads.Count, instanceId, gamepad));
                }
                finally
                {
                    if (gamepad != 0)
                    {
                        SDL.CloseGamepad(gamepad);
                    }
                }
            }

            return gamepads;
        }
        finally
        {
            SDL.QuitSubSystem(SDL.InitFlags.Gamepad);
        }
    }

    private static nint OpenGamepad(uint[] gamepadIds, int count, int deviceIndex)
    {
        return deviceIndex >= count
            ? throw new ArgumentOutOfRangeException(
                nameof(deviceIndex),
                $"SDL gamepad index {deviceIndex} was not found.")
            : SDL.OpenGamepad(gamepadIds[deviceIndex]);
    }

    private static List<SdlGamepadInfo> GetGamepadInfos(uint[] gamepadIds, int count)
    {
        List<SdlGamepadInfo> gamepads = new(count);
        for (int i = 0; i < count; i++)
        {
            uint instanceId = gamepadIds[i];
            nint gamepad = SDL.OpenGamepad(instanceId);

            try
            {
                gamepads.Add(gamepad == 0
                    ? CreateGamepadInfoForId(gamepads.Count, instanceId)
                    : CreateGamepadInfo(gamepads.Count, instanceId, gamepad));
            }
            finally
            {
                if (gamepad != 0)
                {
                    SDL.CloseGamepad(gamepad);
                }
            }
        }

        return gamepads;
    }

    private static void InitializeGamepads()
    {
        SetInputHints();
        if (!SDL.Init(SDL.InitFlags.Gamepad))
        {
            throw new InvalidOperationException($"Could not initialize SDL gamepad input: {SDL.GetError()}");
        }
    }

    private static void ValidateOptions(SdlGamepadOptions options)
    {
        if (options.DeviceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "SDL gamepad index must be non-negative.");
        }

        if (options.MotionDeviceIndex is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "SDL motion gamepad index must be non-negative.");
        }

        if (options.UsePhysicalMotion && options.Mode != SdlGamepadInputMode.Steam)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Physical motion can only be enabled when SDL gamepad mode is Steam.");
        }

        _ = ExpectsSteamInput(options.Mode);
    }

    internal static int ResolveMotionDeviceIndex(
        IReadOnlyList<SdlGamepadInfo> gamepads,
        SdlGamepadInfo primary,
        SdlGamepadOptions options)
    {
        if (options.MotionDeviceIndex.HasValue)
        {
            return options.MotionDeviceIndex.Value;
        }

        int firstPhysicalIndex = -1;
        foreach (SdlGamepadInfo gamepad in gamepads)
        {
            if (gamepad.IsSteamInput)
            {
                continue;
            }

            firstPhysicalIndex = firstPhysicalIndex < 0 ? gamepad.Index : firstPhysicalIndex;
            if (string.Equals(gamepad.Name, primary.Name, StringComparison.OrdinalIgnoreCase))
            {
                return gamepad.Index;
            }
        }

        return firstPhysicalIndex >= 0
            ? firstPhysicalIndex
            : throw new InvalidOperationException(
            "No physical SDL gamepad was found for Steam physical motion. Pass --motion-device-index when a physical motion device is visible.");
    }

    private static void EnsureInputMode(SdlGamepadInfo info, SdlGamepadInputMode mode)
    {
        if (info.IsSteamInput != ExpectsSteamInput(mode))
        {
            throw new InvalidOperationException(
                $"SDL gamepad index {info.Index} is not {DescribeExpectedInput(mode)}. Actual steamHandle={FormatSteamHandle(info.SteamHandle)}.");
        }
    }

    private static void EnsurePhysicalGamepad(SdlGamepadInfo info)
    {
        if (info.IsSteamInput)
        {
            throw new InvalidOperationException(
                $"SDL gamepad index {info.Index} is not a physical SDL gamepad without a Steam handle. Actual steamHandle={FormatSteamHandle(info.SteamHandle)}.");
        }
    }

    private static bool ExpectsSteamInput(SdlGamepadInputMode mode)
    {
        return mode switch
        {
            SdlGamepadInputMode.Physical => false,
            SdlGamepadInputMode.Steam => true,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    private static string DescribeExpectedInput(SdlGamepadInputMode mode)
    {
        return ExpectsSteamInput(mode)
            ? "a Steam-routed SDL gamepad with a Steam handle"
            : "a physical SDL gamepad without a Steam handle";
    }

    private static SdlGamepadInfo CreateGamepadInfo(int index, uint instanceId, nint gamepad)
    {
        string name =
            SDL.GetGamepadName(gamepad) ??
            SDL.GetGamepadNameForID(instanceId) ??
            $"SDL gamepad {instanceId}";

        return new SdlGamepadInfo(
            index,
            instanceId,
            name,
            SDL.GetGamepadSteamHandle(gamepad),
            SDL.GetGamepadVendor(gamepad),
            SDL.GetGamepadProduct(gamepad),
            SDL.GetGamepadPath(gamepad) ?? SDL.GetGamepadPathForID(instanceId))
        {
            HasGyro = SDL.GamepadHasSensor(gamepad, SDL.SensorType.Gyro),
            HasAccelerometer = SDL.GamepadHasSensor(gamepad, SDL.SensorType.Accel),
        };
    }

    private static SdlGamepadInfo CreateGamepadInfoForId(int index, uint instanceId)
    {
        string name =
            SDL.GetGamepadNameForID(instanceId) ??
            $"SDL gamepad {instanceId}";

        return new SdlGamepadInfo(
            index,
            instanceId,
            name,
            0,
            SDL.GetGamepadVendorForID(instanceId),
            SDL.GetGamepadProductForID(instanceId),
            SDL.GetGamepadPathForID(instanceId));
    }

    private static string FormatSteamHandle(ulong steamHandle)
    {
        return steamHandle == 0
            ? "0"
            : $"0x{steamHandle:x16}";
    }

    private static void SetInputHints()
    {
        _ = SDL.SetHint(SDL.Hints.JoystickAllowBackgroundEvents, "1");
    }

    private static GamepadButtons Apply(
        GamepadButtons buttons,
        nint gamepad,
        SDL.GamepadButton sdlButton,
        GamepadButtons outputButton)
    {
        return SDL.GetGamepadButton(gamepad, sdlButton)
            ? buttons | outputButton
            : buttons;
    }

    private unsafe void UpdateMotion(SDL.GamepadSensorEvent sensorEvent)
    {
        ReadOnlySpan<float> data = new(sensorEvent.Data, SensorValueCount);

        SDL.SensorType sensor = (SDL.SensorType)sensorEvent.Sensor;
        if (sensor == SDL.SensorType.Gyro)
        {
            CopyMotion(data, _gyroData);
        }
        else if (sensor == SDL.SensorType.Accel)
        {
            CopyMotion(data, _accelerometerData);
        }
    }

    private static bool EnableSensor(nint gamepad, SDL.SensorType sensor)
    {
        return SDL.GamepadHasSensor(gamepad, sensor) &&
            (SDL.GamepadSensorEnabled(gamepad, sensor) ||
            SDL.SetGamepadSensorEnabled(gamepad, sensor, enabled: true));
    }

    private nint GetRumbleGamepad()
    {
        nint motionGamepad = _motionGamepad;
        return motionGamepad != 0 ? motionGamepad : _gamepad;
    }

    private static bool WaitForSdlEvent(out SDL.Event sdlEvent, CancellationToken cancellationToken)
    {
        sdlEvent = default;
        cancellationToken.ThrowIfCancellationRequested();
        return SDL.WaitEventTimeout(out sdlEvent, EventWaitTimeoutMilliseconds);
    }

    private static void CopyMotion(ReadOnlySpan<float> source, Span<float> destination)
    {
        source.CopyTo(destination);
    }

    private static InvalidOperationException CreateSdlUnavailableException(Exception exception)
    {
        return new InvalidOperationException(
            "SDL3 runtime is not available. Restore SDL3-CS.Native or put SDL3.dll next to the app.",
            exception);
    }
}
