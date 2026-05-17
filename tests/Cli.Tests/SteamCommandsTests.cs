using System.Collections.Generic;
using System.Text.Json;
using Profiles;
using SteamInput;

namespace Cli.Tests;

/// <summary>Tests for Steam CLI helpers.</summary>
[TestClass]
public sealed class SteamCommandsTests
{
    /// <summary>Checks Steam game kind formatting.</summary>
    [TestMethod]
    public void DisplayKindFormatsShortcut()
    {
        Assert.AreEqual("shortcut", SteamCommands.DisplayKind(SteamGameKind.NonSteamShortcut));
    }

    /// <summary>Checks SRM export targets the client profile command.</summary>
    [TestMethod]
    public void SrmExportUsesClientRunLaunchOptions()
    {
        Dictionary<string, GameProfile> profiles = new()
        {
            ["frag punk"] = new GameProfile
            {
                Title = "Frag Punk",
                Executable = @"C:\Games\FragPunk\FragPunk.exe",
            },
        };

        string json = SteamRomManagerExport.CreateJson(profiles, @"C:\Tools\vm\Cli.exe");

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement entry = document.RootElement[0];
        Assert.AreEqual("Frag Punk", entry.GetProperty("title").GetString());
        Assert.AreEqual("client run \"frag punk\"", entry.GetProperty("launchOptions").GetString());
    }
}
