using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Inputs.RawInput;

[SupportedOSPlatform("windows")]
public sealed partial class RawInputMouseSource
{
    // MARK: Window
    // ========================================================================

    private static nint HandleWindowMessage(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == WmInput)
        {
            try
            {
                CurrentState?.HandleRawInput(lParam);
            }
            catch (OperationCanceledException) when (CurrentState?.CancellationToken.IsCancellationRequested == true)
            {
            }

            return wParam == nint.Zero
                ? NativeMethods.DefWindowProc(hwnd, message, wParam, lParam)
                : nint.Zero;
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
                throw new Win32Exception(registerError, "Could not register Raw Input mouse window class.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(classNameHandle);
        }

        nint windowHandle = NativeMethods.CreateWindowEx(
            0,
            WindowClassName,
            "Raw Input mouse source",
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
            0 => throw new Win32Exception(error, "Could not create Raw Input mouse window."),
            _ => windowHandle,
        };
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not process raw mouse input.");
        }
    }

    private const string WindowClassName = "Inputs.RawInput";
    private const int ClassAlreadyRegisteredError = 1410;
}
