using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SteamInput.Tests;

/// <summary>Tests for Steam game catalog reading.</summary>
[TestClass]
public sealed class SteamGameCatalogTests
{
    /// <summary>Checks Steam app manifest parsing across configured libraries.</summary>
    [TestMethod]
    public void ListGamesReturnsSteamAppsFromConfiguredLibraries()
    {
        using TestSteamDirectory steam = TestSteamDirectory.Create();
        string extraLibrary = steam.CreateDirectory("LibraryTwo");
        steam.WriteText(
            "steamapps/libraryfolders.vdf",
            $$"""
            "libraryfolders"
            {
                "0"
                {
                    "path" "{{steam.RootForVdf}}"
                }
                "1"
                {
                    "path" "{{PathForVdf(extraLibrary)}}"
                }
            }
            """);
        steam.WriteText(
            "steamapps/appmanifest_480.acf",
            """
            "AppState"
            {
                "appid" "480"
                "name" "Spacewar"
                "installdir" "Spacewar"
            }
            """);
        steam.WriteText(
            "LibraryTwo/steamapps/appmanifest_20.acf",
            """
            "AppState"
            {
                "appid" "20"
                "name" "Beta"
                "installdir" "Beta"
            }
            """);
        IReadOnlyList<SteamGame> games = SteamInputClient.ListGames(steam.Root);

        Assert.HasCount(2, games);
        Assert.AreEqual<uint>(20, games[0].AppId);
        Assert.AreEqual("Beta", games[0].Name);
        Assert.AreEqual(SteamGameKind.SteamApp, games[0].Kind);
        Assert.AreEqual(Path.Combine(extraLibrary, "steamapps", "common", "Beta"), games[0].LocalPath);
        Assert.AreEqual<uint>(480, games[1].AppId);
        Assert.AreEqual("Spacewar", games[1].Name);
        Assert.AreEqual(Path.Combine(steam.Root, "steamapps", "common", "Spacewar"), games[1].LocalPath);
    }

    /// <summary>Checks non-Steam shortcut parsing.</summary>
    [TestMethod]
    public void ListGamesReturnsNonSteamShortcuts()
    {
        using TestSteamDirectory steam = TestSteamDirectory.Create();
        steam.WriteShortcuts(
            123,
            new ShortcutEntry(
                3_456_789_012,
                "Manual Game",
                "\"C:\\Games\\Manual\\game.exe\"",
                "C:\\Games\\Manual"));

        IReadOnlyList<SteamGame> games = SteamInputClient.ListGames(steam.Root, 123);
        Assert.HasCount(1, games);
        SteamGame game = games[0];

        Assert.AreEqual<uint>(3_456_789_012, game.AppId);
        Assert.AreEqual("Manual Game", game.Name);
        Assert.AreEqual(SteamGameKind.NonSteamShortcut, game.Kind);
        Assert.AreEqual("C:\\Games\\Manual", game.LocalPath);
    }

    /// <summary>Checks combined catalog sorting and optional shortcut reading.</summary>
    [TestMethod]
    public void ListGamesReturnsSteamAppsAndShortcutsSorted()
    {
        using TestSteamDirectory steam = TestSteamDirectory.Create();
        steam.WriteText(
            "steamapps/appmanifest_20.acf",
            """
            "AppState"
            {
                "appid" "20"
                "name" "Beta"
                "installdir" "Beta"
            }
            """);
        steam.WriteShortcuts(
            123,
            new ShortcutEntry(10, "Alpha", "alpha.exe", ""));
        IReadOnlyList<SteamGame> games = SteamInputClient.ListGames(steam.Root, 123);
        IReadOnlyList<SteamGame> steamOnlyGames = SteamInputClient.ListGames(steam.Root);

        Assert.HasCount(2, games);
        Assert.AreEqual("Alpha", games[0].Name);
        Assert.AreEqual("Beta", games[1].Name);
        Assert.HasCount(1, steamOnlyGames);
        Assert.AreEqual("Beta", steamOnlyGames[0].Name);
    }

    private static string PathForVdf(string path)
    {
        return path.Replace("\\", "\\\\", StringComparison.Ordinal);
    }

    private sealed class TestSteamDirectory : IDisposable
    {
        private TestSteamDirectory(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public string RootForVdf => PathForVdf(Root);

        public static TestSteamDirectory Create()
        {
            string root = Path.Combine(Path.GetTempPath(), $"virtual-mouse-steam-{Guid.NewGuid():N}");
            _ = Directory.CreateDirectory(Path.Combine(root, "steamapps"));
            return new TestSteamDirectory(root);
        }

        public string CreateDirectory(string relativePath)
        {
            string path = Path.Combine(Root, relativePath);
            _ = Directory.CreateDirectory(path);
            _ = Directory.CreateDirectory(Path.Combine(path, "steamapps"));
            return path;
        }

        public void WriteText(string relativePath, string content)
        {
            string path = Path.Combine(Root, relativePath);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, Encoding.UTF8);
        }

        public void WriteShortcuts(uint userId, params ShortcutEntry[] entries)
        {
            string path = Path.Combine(
                Root,
                "userdata",
                userId.ToString(CultureInfo.InvariantCulture),
                "config",
                "shortcuts.vdf");
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using FileStream stream = File.Create(path);
            using BinaryWriter writer = new(stream, Encoding.UTF8);

            WriteObjectStart(writer, "shortcuts");
            for (int i = 0; i < entries.Length; i++)
            {
                ShortcutEntry entry = entries[i];
                WriteObjectStart(writer, i.ToString(CultureInfo.InvariantCulture));
                WriteUInt32(writer, "appid", entry.AppId);
                WriteString(writer, "AppName", entry.Name);
                WriteString(writer, "Exe", entry.Exe);
                WriteString(writer, "StartDir", entry.StartDirectory);
                WriteEnd(writer);
            }

            WriteEnd(writer);
            WriteEnd(writer);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static void WriteObjectStart(BinaryWriter writer, string key)
        {
            writer.Write((byte)0x00);
            WriteNullTerminatedString(writer, key);
        }

        private static void WriteString(BinaryWriter writer, string key, string value)
        {
            writer.Write((byte)0x01);
            WriteNullTerminatedString(writer, key);
            WriteNullTerminatedString(writer, value);
        }

        private static void WriteUInt32(BinaryWriter writer, string key, uint value)
        {
            writer.Write((byte)0x02);
            WriteNullTerminatedString(writer, key);
            writer.Write(value);
        }

        private static void WriteEnd(BinaryWriter writer)
        {
            writer.Write((byte)0x08);
        }

        private static void WriteNullTerminatedString(BinaryWriter writer, string value)
        {
            writer.Write(Encoding.UTF8.GetBytes(value));
            writer.Write((byte)0);
        }
    }

    private sealed record ShortcutEntry(
        uint AppId,
        string Name,
        string Exe,
        string StartDirectory);
}
