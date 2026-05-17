using System.Collections.Generic;
using Profiles;

namespace Cli.Tests;

/// <summary>Tests for profile settings helpers.</summary>
[TestClass]
public sealed class ProfileCatalogTests
{
    /// <summary>Checks receiver processes default to the executable file name.</summary>
    [TestMethod]
    public void ResolveDefaultsReceiverProcessToExecutableName()
    {
        Dictionary<string, GameProfile> profiles = new()
        {
            ["frag-punk"] = new GameProfile
            {
                Executable = @"C:\Games\FragPunk\FragPunk.exe",
            },
        };

        ResolvedGameProfile profile = ProfileCatalog.Resolve(profiles, "frag-punk");

        Assert.AreEqual("Frag Punk", profile.Title);
        Assert.AreEqual("FragPunk.exe", profile.ReceiverProcesses[0]);
        Assert.AreEqual(ControllerOutputKind.Xbox360, profile.ControllerOutput);
        Assert.AreEqual(MouseOutputKind.Viiper, profile.MouseOutput);
    }
}
