using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using PhysicalMouse;
using PhysicalMouse.Viiper;

[SupportedOSPlatform("windows")]
internal static partial class SteamNullifier
{
    private static readonly nint MessageOnlyWindow = new(-3);
    private static readonly WindowProc WindowProcDelegate = HandleWindowMessage;
    private static WindowState? CurrentState;

    public static void Run(ViiperPhysicalMouse mouse, CancellationToken cancellationToken)
    {
        WindowState state = new(mouse, cancellationToken);
        CurrentState = state;

        nint windowHandle = CreateWindowHandle();
        using CancellationTokenRegistration registration = cancellationToken.Register(static target =>
        {
            _ = NativeMethods.PostMessage((nint)target!, WmClose, nint.Zero, nint.Zero);
        }, windowHandle);

        try
        {
            RegisterRawInput(windowHandle);
            RunMessageLoop(windowHandle);
        }
        finally
        {
            CurrentState = null;
            if (windowHandle is not 0)
            {
                _ = NativeMethods.DestroyWindow(windowHandle);
            }
        }
    }

    private static nint HandleWindowMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == WmInput)
        {
            HandleRawInput(lParam);
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

        uint size = 0;
        _ = NativeMethods.GetRawInputData(rawInputHandle, Input, nint.Zero, ref size, (uint)Marshal.SizeOf<RawInputHeader>());
        if (size == 0)
        {
            return;
        }

        if (state.InputBuffer.Length < size)
        {
            state.InputBuffer = new byte[size];
        }

        GCHandle pinned = GCHandle.Alloc(state.InputBuffer, GCHandleType.Pinned);
        try
        {
            nint bufferPointer = pinned.AddrOfPinnedObject();
            int read = NativeMethods.GetRawInputData(rawInputHandle, Input, bufferPointer, ref size, (uint)Marshal.SizeOf<RawInputHeader>());
            if (read < 0 || read != (int)size)
            {
                return;
            }

            RawInput rawInput = Marshal.PtrToStructure<RawInput>(bufferPointer);
            if (rawInput.Header.Type != RawInputMouse)
            {
                return;
            }

            RawMouse mouse = rawInput.Mouse;
            int deltaX = mouse.LastX;
            int deltaY = mouse.LastY;
            int wheel = ReadWheel(mouse.ButtonFlags, mouse.ButtonData);
            if (deltaX == 0 && deltaY == 0 && wheel == 0)
            {
                return;
            }

            state.Mouse.SendAsync(CliSteamCommands.Nullify(new MouseReport(MouseButtons.None, deltaX, deltaY, wheel)), state.CancellationToken)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        finally
        {
            pinned.Free();
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

    private static void RunMessageLoop(nint windowHandle)
    {
        int result = NativeMethods.GetMessage(out Message message, windowHandle, 0, 0);
        while (result > 0)
        {
            _ = NativeMethods.TranslateMessage(ref message);
            _ = NativeMethods.DispatchMessage(ref message);
            result = NativeMethods.GetMessage(out message, windowHandle, 0, 0);
        }

        if (result < 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not process Steam mouse input.");
        }
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
            "Steam nullifier",
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
            0 => throw new Win32Exception(error, "Could not create Steam nullifier window."),
            _ => windowHandle,
        };
    }

    private static int ReadWheel(ushort flags, ushort data)
    {
        if ((flags & MouseWheel) == 0)
        {
            return 0;
        }

        short signedData = unchecked((short)data);
        return signedData / WheelDelta;
    }

    private const string WindowClassName = "PhysicalMouse.Cli.SteamNullifier";
    private const int ClassAlreadyRegisteredError = 1410;

    private sealed class WindowState(ViiperPhysicalMouse mouse, CancellationToken cancellationToken)
    {
        public ViiperPhysicalMouse Mouse { get; } = mouse;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public byte[] InputBuffer { get; set; } = [];
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam);
}
