using System.CommandLine;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using VirtualMouse.Inputs.Sdl;

namespace VirtualMouse.Cli;

[SuppressMessage(
    "Globalization",
    "CA1303:Do not pass literals as localized parameters",
    Justification = "Disposable CLI probe output is not localized.")]
internal static class TestCommands
{
    private const string HidHideCli =
        @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe";

    public static Command Create()
    {
        Command test = new("test");
        Command hidhide = new("hidhide", "Disposable HidHide behavior probes.");
        Command selective = new("selective", "Test whether HidHide can hide from one process only.");
        Command steam = new("steam", "Test hiding Steam Input controllers from this process.");
        Command child = new("__hidhide-child");
        Option<string?> device = new("--device")
        {
            Description = "HidHide device instance path to hide. Defaults to the first gaming device.",
        };
        Option<string> role = new("--role")
        {
            Description = "Child process label.",
            DefaultValueFactory = _ => "child",
        };

        selective.Options.Add(device);
        selective.SetAction(RunSelectiveAsync);
        steam.SetAction(RunSteamAsync);
        child.Options.Add(role);
        child.SetAction(PrintVisibleControllers);
        hidhide.Subcommands.Add(selective);
        hidhide.Subcommands.Add(steam);
        test.Subcommands.Add(hidhide);
        test.Subcommands.Add(child);
        return test;
    }

    private static async Task RunSelectiveAsync(
        ParseResult parseResult,
        CancellationToken cancellationToken)
    {
        string device = parseResult.GetValue<string?>("--device") ?? GetFirstGamingDevice();
        string visibleExe = Path.Combine(AppContext.BaseDirectory, "VirtualMouse.HidHide.Visible.exe");
        string hiddenExe = Path.Combine(AppContext.BaseDirectory, "VirtualMouse.HidHide.Hidden.exe");

        try
        {
            PrepareChildExecutable(visibleExe);
            PrepareChildExecutable(hiddenExe);

            string cloakState = ReadHidHide("--cloak-state");
            string inverseState = ReadHidHide("--inv-state");
            bool wasHidden = IsDeviceHidden(device);

            Console.WriteLine("device:");
            Console.WriteLine(device);
            Console.WriteLine();
            Console.WriteLine("before:");
            await PrintChildReportAsync("visible", visibleExe, cancellationToken).ConfigureAwait(false);
            await PrintChildReportAsync("hidden", hiddenExe, cancellationToken).ConfigureAwait(false);

            RunHidHide("--inv-on", "--cloak-on", "--dev-hide", device, "--app-reg", hiddenExe);

            Console.WriteLine();
            Console.WriteLine("after hiding from hidden child:");
            await PrintChildReportAsync("visible", visibleExe, cancellationToken).ConfigureAwait(false);
            await PrintChildReportAsync("hidden", hiddenExe, cancellationToken).ConfigureAwait(false);

            RestoreHidHide(device, hiddenExe, cloakState, inverseState, wasHidden);
        }
        finally
        {
            TryDeleteFile(visibleExe);
            TryDeleteFile(hiddenExe);
        }
    }

    private static void PrintVisibleControllers(ParseResult parseResult)
    {
        string? role = parseResult.GetValue<string>("--role");
        Console.WriteLine($"{role}:");
        IReadOnlyList<SdlControllerInfo> controllers = SdlControllerCatalog.GetControllers();
        Console.WriteLine($"controllers={controllers.Count.ToString(CultureInfo.InvariantCulture)}");
        foreach (SdlControllerInfo controller in controllers)
        {
            Console.WriteLine(
                $"  {controller.Name} source={controller.Source} vid=0x{controller.VendorId:x4} " +
                $"pid=0x{controller.ProductId:x4} id={controller.Id}");
        }
    }

    private static void RunSteamAsync(ParseResult parseResult)
    {
        _ = parseResult;
        string reportPath = Path.Combine(AppContext.BaseDirectory, "hidhide-steam-report.txt");
        StringBuilder report = new();

        try
        {
            string cloakState = ReadHidHide("--cloak-state");
            string inverseState = ReadHidHide("--inv-state");
            string currentExe = Environment.ProcessPath ??
                throw new InvalidOperationException("Could not resolve the current executable path.");
            bool wasRegistered = IsAppRegistered(currentExe);

            IReadOnlyList<SdlControllerInfo> before = SdlControllerCatalog.GetControllers();
            List<HidHideDevice> devices = GetHidHideDevices();
            List<string> devicesToHide = MatchSteamControllers(before, devices);
            Dictionary<string, bool> wasHidden = [];
            foreach (string device in devicesToHide)
            {
                wasHidden[device] = IsDeviceHidden(device);
            }

            WriteLine(report, "before:");
            PrintControllers(report, before);
            WriteLine(report, string.Empty);
            WriteLine(report, "hidhide devices:");
            PrintHidHideDevices(report, devices);
            WriteLine(report, string.Empty);
            WriteLine(report, "matched Steam Input HidHide devices:");
            PrintDevices(report, devicesToHide);

            if (devicesToHide.Count == 0)
            {
                WriteLine(report, string.Empty);
                WriteLine(report, "No SDL Steam controllers matched HidHide devices.");
                File.WriteAllText(reportPath, report.ToString());
                ShowProbeMessage("HidHide Steam probe completed. No Steam Input devices matched.");
                return;
            }

            try
            {
                List<string> args = ["--inv-on", "--cloak-on", "--app-unreg", currentExe];
                foreach (string device in devicesToHide)
                {
                    args.Add("--dev-hide");
                    args.Add(device);
                }

                RunHidHide([.. args]);

                WriteLine(report, string.Empty);
                WriteLine(report, "after hiding from this process:");
                PrintControllers(report, SdlControllerCatalog.GetControllers());
            }
            finally
            {
                RestoreSteamProbe(currentExe, wasRegistered, cloakState, inverseState, wasHidden);
            }

            File.WriteAllText(reportPath, report.ToString());
            ShowProbeMessage("HidHide Steam probe completed. Report saved as hidhide-steam-report.txt.");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            WriteLine(report, "error:");
            WriteLine(report, exception.ToString());
            File.WriteAllText(reportPath, report.ToString());
            ShowProbeMessage("HidHide Steam probe failed. Report saved as hidhide-steam-report.txt.");
        }
    }

    private static async Task PrintChildReportAsync(
        string role,
        string executable,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new()
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("test");
        start.ArgumentList.Add("__hidhide-child");
        start.ArgumentList.Add("--role");
        start.ArgumentList.Add(role);
        start.Environment["VIRTUALMOUSE_NO_CONSOLE_ATTACH"] = "1";

        using Process process = Process.Start(start) ??
            throw new InvalidOperationException($"Could not start {executable}.");
        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        string error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        await Console.Out.WriteAsync(output.AsMemory(), cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(error))
        {
            await Console.Error.WriteAsync(error.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string GetFirstGamingDevice()
    {
        foreach (HidHideDevice device in GetHidHideDevices())
        {
            if (!string.IsNullOrWhiteSpace(device.DeviceInstancePath))
            {
                return device.DeviceInstancePath;
            }
        }

        throw new InvalidOperationException("HidHide did not report any gaming HID devices.");
    }

    private static List<string> MatchSteamControllers(
        IReadOnlyList<SdlControllerInfo> controllers,
        IReadOnlyList<HidHideDevice> devices)
    {
        List<string> matched = [];
        foreach (SdlControllerInfo controller in controllers)
        {
            if (controller.Source != SdlControllerSource.Steam || string.IsNullOrWhiteSpace(controller.Path))
            {
                continue;
            }

            HidHideDevice? device = FindDeviceBySymbolicLink(devices, controller.Path);
            if (device?.DeviceInstancePath is { Length: > 0 } path)
            {
                matched.Add(path);
            }
        }

        return matched;
    }

    private static HidHideDevice? FindDeviceBySymbolicLink(
        IReadOnlyList<HidHideDevice> devices,
        string symbolicLink)
    {
        string normalized = NormalizeDevicePath(symbolicLink);
        foreach (HidHideDevice device in devices)
        {
            if (NormalizeDevicePath(device.SymbolicLink) == normalized)
            {
                return device;
            }
        }

        return null;
    }

    private static List<HidHideDevice> GetHidHideDevices()
    {
        string output = ReadHidHide("--dev-all");
        using JsonDocument document = JsonDocument.Parse(output);
        List<HidHideDevice> devices = [];
        foreach (JsonElement group in document.RootElement.EnumerateArray())
        {
            if (!group.TryGetProperty("devices", out JsonElement children))
            {
                continue;
            }

            foreach (JsonElement device in children.EnumerateArray())
            {
                devices.Add(new HidHideDevice(
                    GetBool(device, "present"),
                    GetBool(device, "gamingDevice"),
                    GetString(group, "friendlyName"),
                    GetString(device, "vendor"),
                    GetString(device, "product"),
                    GetString(device, "usage"),
                    GetString(device, "symbolicLink"),
                    GetString(device, "deviceInstancePath")));
            }
        }

        return devices;
    }

    private static string GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static bool GetBool(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value) &&
            value.ValueKind == JsonValueKind.True;
    }

    private static void PrintControllers(StringBuilder report, IReadOnlyList<SdlControllerInfo> controllers)
    {
        WriteLine(report, $"controllers={controllers.Count.ToString(CultureInfo.InvariantCulture)}");
        foreach (SdlControllerInfo controller in controllers)
        {
            WriteLine(
                report,
                $"  {controller.Name} source={controller.Source} vid=0x{controller.VendorId:x4} " +
                $"pid=0x{controller.ProductId:x4} id={controller.Id}");
        }
    }

    private static void PrintDevices(StringBuilder report, List<string> devices)
    {
        if (devices.Count == 0)
        {
            WriteLine(report, "  none");
            return;
        }

        foreach (string device in devices)
        {
            WriteLine(report, $"  {device}");
        }
    }

    private static void PrintHidHideDevices(StringBuilder report, IReadOnlyList<HidHideDevice> devices)
    {
        foreach (HidHideDevice device in devices)
        {
            WriteLine(
                report,
                $"  present={device.Present} gaming={device.GamingDevice} name=\"{device.FriendlyName}\" " +
                $"vendor=\"{device.Vendor}\" product=\"{device.Product}\" usage=\"{device.Usage}\"");
            WriteLine(report, $"    symbolic={device.SymbolicLink}");
            WriteLine(report, $"    instance={device.DeviceInstancePath}");
        }
    }

    private static void WriteLine(StringBuilder? report, string value)
    {
        Console.WriteLine(value);
        _ = report?.AppendLine(value);
    }

    private static void ShowProbeMessage(string message)
    {
        _ = System.Windows.MessageBox.Show(
            message,
            "Virtual Mouse",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static void PrepareChildExecutable(string destination)
    {
        string source = Environment.ProcessPath ??
            throw new InvalidOperationException("Could not resolve the current executable path.");
        File.Copy(source, destination, overwrite: true);
    }

    private static void RestoreHidHide(
        string device,
        string hiddenExe,
        string cloakState,
        string inverseState,
        bool wasHidden)
    {
        List<string> args = ["--app-unreg", hiddenExe];
        if (!wasHidden)
        {
            args.Add("--dev-unhide");
            args.Add(device);
        }

        args.Add(IsOn(cloakState) ? "--cloak-on" : "--cloak-off");
        args.Add(IsOn(inverseState) ? "--inv-on" : "--inv-off");
        RunHidHide([.. args]);
    }

    private static bool IsDeviceHidden(string device)
    {
        string output = ReadHidHide("--dev-list");
        return output.Contains(device, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAppRegistered(string path)
    {
        string output = ReadHidHide("--app-list");
        return output.Contains(path, StringComparison.OrdinalIgnoreCase);
    }

    private static void RestoreSteamProbe(
        string currentExe,
        bool wasRegistered,
        string cloakState,
        string inverseState,
        IReadOnlyDictionary<string, bool> wasHidden)
    {
        List<string> args = wasRegistered
            ? ["--app-reg", currentExe]
            : ["--app-unreg", currentExe];
        foreach ((string device, bool hidden) in wasHidden)
        {
            args.Add(hidden ? "--dev-hide" : "--dev-unhide");
            args.Add(device);
        }

        args.Add(IsOn(cloakState) ? "--cloak-on" : "--cloak-off");
        args.Add(IsOn(inverseState) ? "--inv-on" : "--inv-off");
        RunHidHide([.. args]);
    }

    private static bool IsOn(string value)
    {
        return value.Contains("yes", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("true", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("on", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDevicePath(string value)
    {
        return value.Replace("\\", "#", StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
    }

    private static string ReadHidHide(params string[] args)
    {
        return RunProcess(HidHideCli, args);
    }

    private static void RunHidHide(params string[] args)
    {
        _ = RunProcess(HidHideCli, args);
    }

    private static string RunProcess(string fileName, IReadOnlyList<string> args)
    {
        _ = File.Exists(fileName)
            ? true
            : throw new FileNotFoundException("HidHideCLI.exe was not found.", fileName);

        ProcessStartInfo start = new()
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(start) ??
            throw new InvalidOperationException($"Could not start {fileName}.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? output
            : throw new InvalidOperationException(
                $"{Path.GetFileName(fileName)} failed with exit code {process.ExitCode}: {error}{output}");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record HidHideDevice(
        bool Present,
        bool GamingDevice,
        string FriendlyName,
        string Vendor,
        string Product,
        string Usage,
        string SymbolicLink,
        string DeviceInstancePath);
}
