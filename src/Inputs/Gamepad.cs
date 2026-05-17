using System;
using System.Threading;

namespace Inputs;

/// <summary>Reads standard gamepad reports from an input source.</summary>
public interface IGamepadInputSource : IAsyncDisposable
{
    /// <summary>Gets whether the input source is connected.</summary>
    bool IsConnected { get; }

    /// <summary>Runs the input loop until cancelled.</summary>
    /// <param name="handler">Called for each gamepad state update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    void Run(GamepadInputHandler handler, CancellationToken cancellationToken = default);
}

/// <summary>Sends rumble feedback to a gamepad.</summary>
public interface IGamepadRumbleSink
{
    /// <summary>Attempts to set gamepad rumble.</summary>
    /// <param name="rumble">Rumble state.</param>
    /// <returns><see langword="true" /> when the source accepted the rumble state.</returns>
    bool TryRumble(GamepadRumble rumble);
}

/// <summary>Handles one gamepad input update.</summary>
/// <param name="input">Gamepad input.</param>
public delegate void GamepadInputHandler(in GamepadInput input);

/// <summary>Gamepad input from one source.</summary>
/// <param name="State">Gamepad state.</param>
/// <param name="DeviceName">Source device name, when known.</param>
public readonly record struct GamepadInput(GamepadState State, string DeviceName);

/// <summary>Gamepad rumble state.</summary>
/// <param name="LowFrequency">Low-frequency motor intensity.</param>
/// <param name="HighFrequency">High-frequency motor intensity.</param>
public readonly record struct GamepadRumble(ushort LowFrequency, ushort HighFrequency)
{
    /// <summary>No rumble.</summary>
    public static GamepadRumble Empty => default;

    /// <summary>Gets whether both rumble motors are stopped.</summary>
    public bool IsEmpty => LowFrequency == 0 && HighFrequency == 0;
}

/// <summary>Standard gamepad button flags.</summary>
[Flags]
public enum GamepadButtons
{
    /// <summary>No buttons.</summary>
    None = 0,
    /// <summary>Bottom face button.</summary>
    South = 1 << 0,
    /// <summary>Right face button.</summary>
    East = 1 << 1,
    /// <summary>Left face button.</summary>
    West = 1 << 2,
    /// <summary>Top face button.</summary>
    North = 1 << 3,
    /// <summary>Back button.</summary>
    Back = 1 << 4,
    /// <summary>Guide button.</summary>
    Guide = 1 << 5,
    /// <summary>Start button.</summary>
    Start = 1 << 6,
    /// <summary>Left stick button.</summary>
    LeftStick = 1 << 7,
    /// <summary>Right stick button.</summary>
    RightStick = 1 << 8,
    /// <summary>Left shoulder button.</summary>
    LeftShoulder = 1 << 9,
    /// <summary>Right shoulder button.</summary>
    RightShoulder = 1 << 10,
    /// <summary>D-pad up.</summary>
    DPadUp = 1 << 11,
    /// <summary>D-pad down.</summary>
    DPadDown = 1 << 12,
    /// <summary>D-pad left.</summary>
    DPadLeft = 1 << 13,
    /// <summary>D-pad right.</summary>
    DPadRight = 1 << 14,
}

/// <summary>Standard gamepad state.</summary>
/// <param name="Buttons">Pressed buttons.</param>
/// <param name="LeftX">Left stick horizontal axis.</param>
/// <param name="LeftY">Left stick vertical axis.</param>
/// <param name="RightX">Right stick horizontal axis.</param>
/// <param name="RightY">Right stick vertical axis.</param>
/// <param name="LeftTrigger">Left trigger axis.</param>
/// <param name="RightTrigger">Right trigger axis.</param>
/// <param name="Motion">Motion sensor state.</param>
public readonly record struct GamepadState(
    GamepadButtons Buttons,
    short LeftX,
    short LeftY,
    short RightX,
    short RightY,
    ushort LeftTrigger,
    ushort RightTrigger,
    GamepadMotion Motion)
{
    /// <summary>Centered gamepad state.</summary>
    public static GamepadState Empty => default;
}

/// <summary>Gamepad motion sensor state.</summary>
/// <param name="HasGyro">Whether gyro values are present.</param>
/// <param name="GyroX">Gyro X value.</param>
/// <param name="GyroY">Gyro Y value.</param>
/// <param name="GyroZ">Gyro Z value.</param>
/// <param name="HasAccelerometer">Whether accelerometer values are present.</param>
/// <param name="AccelX">Accelerometer X value.</param>
/// <param name="AccelY">Accelerometer Y value.</param>
/// <param name="AccelZ">Accelerometer Z value.</param>
public readonly record struct GamepadMotion(
    bool HasGyro,
    float GyroX,
    float GyroY,
    float GyroZ,
    bool HasAccelerometer,
    float AccelX,
    float AccelY,
    float AccelZ);
