using System.Collections.Generic;
using VirtualMouse.HidHide;

namespace VirtualMouse.Tests;

/// <summary>Tests HidHide profile scoping.</summary>
[TestClass]
public sealed class HidHideProfileFirewallTests
{
    private static readonly string[] ApplyThenClearCommands =
    [
        "--dev-list",
        "--app-list",
        "--cloak-state",
        "--inv-state",
        "--inv-on --cloak-on --dev-hide dev-1 --app-reg app-1",
        "--app-unreg app-1 --dev-unhide dev-1 --cloak-off --inv-off",
    ];

    /// <summary>Applies inverse-mode scope and restores previous device/app state.</summary>
    [TestMethod]
    public void ApplyThenClearRestoresPreviousState()
    {
        FakeRunner runner = new(
            devList: "",
            appList: "",
            cloakState: "off",
            inverseState: "off");
        using HidHideProfileFirewall firewall = new(runner);

        firewall.Apply(HidHideScope.Create(["dev-1"], ["app-1"]));
        firewall.Clear();

        CollectionAssert.AreEqual(ApplyThenClearCommands, runner.Commands);
    }

    /// <summary>Clear restores pre-existing registrations instead of removing them.</summary>
    [TestMethod]
    public void ClearRestoresExistingHiddenDeviceAndRegisteredApp()
    {
        FakeRunner runner = new(
            devList: "dev-1",
            appList: "app-1",
            cloakState: "on",
            inverseState: "on");
        using HidHideProfileFirewall firewall = new(runner);

        firewall.Apply(HidHideScope.Create(["dev-1"], ["app-1"]));
        firewall.Clear();

        Assert.AreEqual(
            "--app-reg app-1 --dev-hide dev-1 --cloak-on --inv-on",
            runner.Commands[^1]);
    }

    /// <summary>Matches a transport path to HidHide's device instance path.</summary>
    [TestMethod]
    public void FindDeviceInstancePathMatchesSymbolicLink()
    {
        const string Devices = """
            [
              {
                "friendlyName": "DualSense",
                "devices": [
                  {
                    "present": true,
                    "gamingDevice": true,
                    "vendor": "054C",
                    "product": "0CE6",
                    "usage": "0005",
                    "symbolicLink": "\\\\?\\HID#VID_054C&PID_0CE6#ABC",
                    "deviceInstancePath": "HID\\VID_054C&PID_0CE6\\ABC"
                  }
                ]
              }
            ]
            """;
        HidHideDeviceCatalog catalog = new(new DeviceListRunner(Devices));

        string? path = catalog.FindDeviceInstancePath(@"\\?\hid\vid_054c&pid_0ce6\abc");

        Assert.AreEqual(@"HID\VID_054C&PID_0CE6\ABC", path);
    }

    private sealed class FakeRunner(
        string devList,
        string appList,
        string cloakState,
        string inverseState) : IHidHideCommandRunner
    {
        public List<string> Commands { get; } = [];

        public string Run(IReadOnlyList<string> args)
        {
            string command = string.Join(" ", args);
            Commands.Add(command);
            return command switch
            {
                "--dev-list" => devList,
                "--app-list" => appList,
                "--cloak-state" => cloakState,
                "--inv-state" => inverseState,
                _ => "",
            };
        }
    }

    private sealed class DeviceListRunner(string devices) : IHidHideCommandRunner
    {
        public string Run(IReadOnlyList<string> args)
        {
            return string.Join(" ", args) == "--dev-all"
                ? devices
                : "";
        }
    }
}
