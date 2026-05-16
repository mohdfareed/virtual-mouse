using System.Runtime.InteropServices;

namespace Inputs.Sdl;

internal enum SdlGamepadAxis
{
    LeftX = 0,
    LeftY = 1,
    RightX = 2,
    RightY = 3,
    LeftTrigger = 4,
    RightTrigger = 5,
}

internal enum SdlGamepadButton
{
    South = 0,
    East = 1,
    West = 2,
    North = 3,
    Back = 4,
    Guide = 5,
    Start = 6,
    LeftStick = 7,
    RightStick = 8,
    LeftShoulder = 9,
    RightShoulder = 10,
    DPadUp = 11,
    DPadDown = 12,
    DPadLeft = 13,
    DPadRight = 14,
}

internal static partial class SdlNative
{
    internal const uint InitGamepad = 0x00002000;
    private const string LibraryName = "SDL3";

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SDL_Init(uint flags);

    [LibraryImport(LibraryName)]
    internal static partial void SDL_QuitSubSystem(uint flags);

    [LibraryImport(LibraryName)]
    internal static partial nint SDL_GetGamepads(out int count);

    [LibraryImport(LibraryName)]
    internal static partial void SDL_free(nint mem);

    [LibraryImport(LibraryName)]
    internal static partial nint SDL_OpenGamepad(uint instanceId);

    [LibraryImport(LibraryName)]
    internal static partial void SDL_CloseGamepad(nint gamepad);

    [LibraryImport(LibraryName)]
    internal static partial nint SDL_GetGamepadName(nint gamepad);

    [LibraryImport(LibraryName)]
    internal static partial nint SDL_GetGamepadNameForID(uint instanceId);

    [LibraryImport(LibraryName)]
    internal static partial void SDL_UpdateGamepads();

    [LibraryImport(LibraryName)]
    internal static partial short SDL_GetGamepadAxis(nint gamepad, SdlGamepadAxis axis);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SDL_GetGamepadButton(nint gamepad, SdlGamepadButton button);

    [LibraryImport(LibraryName)]
    internal static partial nint SDL_GetError();

    internal static string GetError()
    {
        return ToUtf8String(SDL_GetError()) ?? "Unknown SDL error.";
    }

    internal static string? ToUtf8String(nint value)
    {
        return value == 0 ? null : Marshal.PtrToStringUTF8(value);
    }
}
