using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using PhysicalMouse;
using PhysicalMouse.Viiper;

[SupportedOSPlatform("windows")]
internal static partial class SteamNullifier
{
    private static readonly int RawInputBufferInitialSize = Marshal.SizeOf<RawInput>();
    private static readonly nint MessageOnlyWindow = new(-3);
    private static readonly WindowProc WindowProcDelegate = HandleWindowMessage;
    private static WindowState? CurrentState;

    public static void Run(ViiperPhysicalMouse mouse, SteamMouseMode mode, CancellationToken cancellationToken)
    {
        WindowState state = new(mouse, mode, cancellationToken);
        CurrentState = state;

        nint windowHandle = CreateWindowHandle();
        using CancellationTokenRegistration registration = cancellationToken.Register(static target =>
        {
            _ = NativeMethods.PostMessage((nint)target!, WmClose, nint.Zero, nint.Zero);
        }, windowHandle);

        try
        {
            RegisterRawInput(windowHandle);
            RunMessageLoop();
        }
        finally
        {
            CurrentState = null;
            if (windowHandle is not 0)
            {
                _ = NativeMethods.DestroyWindow(windowHandle);
            }

            state.Dispose();
        }
    }

    private static nint HandleWindowMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == WmInput)
        {
            try
            {
                HandleRawInput(lParam);
            }
            catch (OperationCanceledException) when (CurrentState?.CancellationToken.IsCancellationRequested == true)
            {
            }

            return nint.Zero;
        }

        if (message == WmClose)
        {
            _ = NativeMethods.DestroyWindow(hwnd);
            return nint.Zero;
        }

        if (message == WmDestroy)
        {
            NativeMethods.PostQuitMessage(0);
            return nint.Zero;
        }

        return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static void HandleRawInput(nint rawInputHandle)
    {
        WindowState state = CurrentState ?? throw new InvalidOperationException("Steam nullifier is not running.");
        if (!TryReadRawInput(rawInputHandle, state, out RawInput rawInput))
        {
            return;
        }

        if (rawInput.Header.Type != RawInputMouse)
        {
            return;
        }

        if (IsOwnedDevice(rawInput.Header.Device, state))
        {
            return;
        }

        RawMouse mouse = rawInput.Mouse;
        int deltaX = mouse.LastX;
        int deltaY = mouse.LastY;
        int wheelDelta = GetWheelDelta(mouse.ButtonFlags, mouse.ButtonData);
        bool hasButtonEvent = HasMouseButtonEvent(mouse.ButtonFlags);
        if (deltaX == 0 && deltaY == 0 && !hasButtonEvent && wheelDelta == 0)
        {
            return;
        }

        MouseReport input = state.CreateReport(mouse.ButtonFlags, deltaX, deltaY, wheelDelta);
        MouseReport output = CliSteamCommands.ApplyMode(input, state.Mode);
        if (output.IsEmpty)
        {
            return;
        }

        state.CancellationToken.ThrowIfCancellationRequested();
        SendSynchronously(state.Mouse, output, state.CancellationToken);
    }

    private static void SendSynchronously(ViiperPhysicalMouse mouse, MouseReport report, CancellationToken cancellationToken)
    {
        ValueTask sendTask = mouse.SendAsync(report, cancellationToken);
        if (sendTask.IsCompleted)
        {
            sendTask.GetAwaiter().GetResult();
            return;
        }

        sendTask.AsTask().GetAwaiter().GetResult();
    }

    private static bool TryReadRawInput(nint rawInputHandle, WindowState state, out RawInput rawInput)
    {
        state.EnsureInputBuffer((uint)RawInputBufferInitialSize);

        uint size = state.InputBufferSize;
        int read = NativeMethods.GetRawInputData(
            rawInputHandle,
            Input,
            state.InputBuffer,
            ref size,
            (uint)Marshal.SizeOf<RawInputHeader>());

        if (read < 0)
        {
            uint requiredSize = 0;
            _ = NativeMethods.GetRawInputData(
                rawInputHandle,
                Input,
                nint.Zero,
                ref requiredSize,
                (uint)Marshal.SizeOf<RawInputHeader>());

            if (requiredSize == 0)
            {
                rawInput = default;
                return false;
            }

            state.EnsureInputBuffer(requiredSize);
            size = state.InputBufferSize;
            read = NativeMethods.GetRawInputData(
                rawInputHandle,
                Input,
                state.InputBuffer,
                ref size,
                (uint)Marshal.SizeOf<RawInputHeader>());
        }

        if (read < RawInputBufferInitialSize)
        {
            rawInput = default;
            return false;
        }

        rawInput = Marshal.PtrToStructure<RawInput>(state.InputBuffer);
        return true;
    }

    private static bool IsOwnedDevice(nint device, WindowState state)
    {
        if (device == nint.Zero)
        {
            return false;
        }

        if (!state.DeviceNames.TryGetValue(device, out string? deviceName))
        {
            deviceName = GetDeviceName(device);
            state.DeviceNames[device] = deviceName;
        }

        return deviceName.Contains(OwnedDeviceFragment, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDeviceName(nint device)
    {
        uint size = 0;
        _ = NativeMethods.GetRawInputDeviceInfo(device, DeviceName, nint.Zero, ref size);
        if (size == 0)
        {
            return string.Empty;
        }

        nint buffer = Marshal.AllocHGlobal((int)(size * sizeof(char)));
        try
        {
            uint result = NativeMethods.GetRawInputDeviceInfo(device, DeviceName, buffer, ref size);
            return result == uint.MaxValue
                ? string.Empty
                : Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void RegisterRawInput(nint windowHandle)
    {
        RawInputDevice[] devices =
        [
            new()
            {
                UsagePage = UsagePageGenericDesktop,
                Usage = UsageMouse,
                Flags = RawInputSink,
                Target = windowHandle,
            }
        ];

        if (!NativeMethods.RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register raw mouse input.");
        }
    }

    private static void RunMessageLoop()
    {
        int result = NativeMethods.GetMessage(out Message message, nint.Zero, 0, 0);
        while (result > 0)
        {
            _ = NativeMethods.TranslateMessage(ref message);
            _ = NativeMethods.DispatchMessage(ref message);
            result = NativeMethods.GetMessage(out message, nint.Zero, 0, 0);
        }

        if (result < 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not process Steam mouse input.");
        }
    }

    private static bool HasMouseButtonEvent(ushort flags)
    {
        const ushort buttonMask =
            0x0001 | 0x0002 |
            0x0004 | 0x0008 |
            0x0010 | 0x0020 |
            0x0040 | 0x0080 |
            0x0100 | 0x0200;

        return (flags & buttonMask) != 0;
    }

    private static nint CreateWindowHandle()
    {
        nint classNameHandle = Marshal.StringToHGlobalUni(WindowClassName);
        WindowClassEx windowClass = new()
        {
            ClassName = classNameHandle,
            MenuName = nint.Zero,
            Instance = NativeMethods.GetModuleHandle(null),
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            WindowProc = Marshal.GetFunctionPointerForDelegate(WindowProcDelegate),
        };

        try
        {
            int registerError = NativeMethods.RegisterClassEx(ref windowClass) == 0 ? Marshal.GetLastWin32Error() : 0;
            if (registerError is not 0 and not ClassAlreadyRegisteredError)
            {
                throw new Win32Exception(registerError, "Could not register Steam nullifier window class.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(classNameHandle);
        }

        nint windowHandle = NativeMethods.CreateWindowEx(
            0,
            WindowClassName,
            "Steam mouse bridge",
            0,
            0,
            0,
            0,
            0,
            MessageOnlyWindow,
            nint.Zero,
            windowClass.Instance,
            nint.Zero);

        int error = Marshal.GetLastWin32Error();
        return windowHandle switch
        {
            0 => throw new Win32Exception(error, "Could not create Steam mouse bridge window."),
            _ => windowHandle,
        };
    }

    private const string WindowClassName = "PhysicalMouse.Cli.SteamNullifier";
    private const int ClassAlreadyRegisteredError = 1410;
    private const string OwnedDeviceFragment = "VID_6969&PID_5050";

    private sealed class WindowState(
        ViiperPhysicalMouse mouse,
        SteamMouseMode mode,
        CancellationToken cancellationToken) : IDisposable
    {
        private MouseButtons currentButtons;

        public ViiperPhysicalMouse Mouse { get; } = mouse;

        public SteamMouseMode Mode { get; } = mode;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public Dictionary<nint, string> DeviceNames { get; } = [];

        public nint InputBuffer { get; private set; }

        public uint InputBufferSize { get; private set; }

        public void EnsureInputBuffer(uint size)
        {
            if (InputBuffer != nint.Zero && InputBufferSize >= size)
            {
                return;
            }

            if (InputBuffer != nint.Zero)
            {
                Marshal.FreeHGlobal(InputBuffer);
            }

            InputBufferSize = Math.Max(size, (uint)RawInputBufferInitialSize);
            InputBuffer = Marshal.AllocHGlobal((int)InputBufferSize);
        }

        public MouseReport CreateReport(ushort buttonFlags, int deltaX, int deltaY, int wheelDelta)
        {
            CancellationToken.ThrowIfCancellationRequested();

            if (buttonFlags != 0)
            {
                currentButtons = ApplyButton(currentButtons, buttonFlags, 0x0001, 0x0002, MouseButtons.Left);
                currentButtons = ApplyButton(currentButtons, buttonFlags, 0x0004, 0x0008, MouseButtons.Right);
                currentButtons = ApplyButton(currentButtons, buttonFlags, 0x0010, 0x0020, MouseButtons.Middle);
                currentButtons = ApplyButton(currentButtons, buttonFlags, 0x0040, 0x0080, MouseButtons.Back);
                currentButtons = ApplyButton(currentButtons, buttonFlags, 0x0100, 0x0200, MouseButtons.Forward);
            }

            return new MouseReport(currentButtons, deltaX, deltaY, wheelDelta);
        }

        private static MouseButtons ApplyButton(
            MouseButtons buttons,
            ushort flags,
            ushort downFlag,
            ushort upFlag,
            MouseButtons button)
        {
            return (flags & downFlag) != 0
                ? buttons | button
                : (flags & upFlag) != 0
                    ? buttons & ~button
                    : buttons;
        }

        public void Dispose()
        {
            if (InputBuffer != nint.Zero)
            {
                Marshal.FreeHGlobal(InputBuffer);
                InputBuffer = nint.Zero;
                InputBufferSize = 0;
            }
        }
    }

    private const ushort RI_MOUSE_WHEEL = 0x0400;
    private const int WHEEL_DELTA = 120;

    private static int GetWheelDelta(ushort flags, ushort buttonData)
    {
        return (flags & RI_MOUSE_WHEEL) == 0
            ? 0
            : unchecked((short)buttonData) / WHEEL_DELTA;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam);
}
