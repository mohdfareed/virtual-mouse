using System;
using System.Windows;

namespace VirtualMouse.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application app = new()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        using AppContext context = AppContext.Create();
        context.Start();
        _ = app.Run();
    }
}
