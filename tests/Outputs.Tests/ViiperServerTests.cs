using System;
using System.IO;
using Outputs.Viiper;

namespace Outputs.Tests;

/// <summary>Tests for VIIPER server helpers.</summary>
[TestClass]
public sealed class ViiperServerTests
{
    /// <summary>Checks configured executable path resolution.</summary>
    [TestMethod]
    public void FindExecutablePathReturnsConfiguredPath()
    {
        string directory = CreateTempDirectory();
        try
        {
            string executablePath = Path.Combine(directory, "custom-viiper.exe");
            File.WriteAllText(executablePath, string.Empty);

            Assert.AreEqual(
                Path.GetFullPath(executablePath),
                ViiperServer.FindExecutablePath(executablePath, localApplicationData: null));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Checks default local app data executable path resolution.</summary>
    [TestMethod]
    public void FindExecutablePathReturnsLocalAppDataPath()
    {
        string directory = CreateTempDirectory();
        try
        {
            string executablePath = Path.Combine(directory, "VIIPER", "viiper.exe");
            _ = Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
            File.WriteAllText(executablePath, string.Empty);

            Assert.AreEqual(
                Path.GetFullPath(executablePath),
                ViiperServer.FindExecutablePath(configuredPath: null, directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Checks missing executable path handling.</summary>
    [TestMethod]
    public void FindExecutablePathReturnsNullForMissingPath()
    {
        Assert.IsNull(ViiperServer.FindExecutablePath("C:\\missing\\viiper.exe", localApplicationData: null));
    }

    /// <summary>Checks local host detection.</summary>
    [TestMethod]
    public void IsLocalHostRecognizesLoopbackHosts()
    {
        Assert.IsTrue(ViiperServer.IsLocalHost("localhost"));
        Assert.IsTrue(ViiperServer.IsLocalHost("127.0.0.1"));
        Assert.IsTrue(ViiperServer.IsLocalHost("::1"));
        Assert.IsFalse(ViiperServer.IsLocalHost("192.168.0.10"));
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"virtual-mouse-viiper-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(directory);
        return directory;
    }
}
