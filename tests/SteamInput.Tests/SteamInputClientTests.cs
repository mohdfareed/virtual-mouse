using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SteamInput.Tests;

/// <summary>Tests for Steam Input URL control.</summary>
[TestClass]
public sealed class SteamInputClientTests
{
    /// <summary>Checks Steam path validation for catalog reads.</summary>
    [TestMethod]
    public void ListGamesRejectsMissingSteamPath()
    {
        _ = Assert.ThrowsExactly<InvalidOperationException>(
            () => SteamInputClient.ListGames(Path.Combine(Path.GetTempPath(), $"missing-steam-{Guid.NewGuid():N}")));
    }

    /// <summary>Checks force URL shape.</summary>
    [TestMethod]
    public async Task ForceAsyncOpensForceInputAppIdUrl()
    {
        Uri? openedUrl = null;
        SteamInputClient client = new((url, _) =>
        {
            openedUrl = url;
            return ValueTask.CompletedTask;
        });

        await client.ForceAsync(789).ConfigureAwait(false);

        Assert.AreEqual("steam://forceinputappid/789", openedUrl?.AbsoluteUri);
    }

    /// <summary>Checks force input validation.</summary>
    [TestMethod]
    public async Task ForceAsyncRejectsZeroAppId()
    {
        SteamInputClient client = new();

        _ = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            async () => await client.ForceAsync(0).ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    /// <summary>Checks desktop force URL shape.</summary>
    [TestMethod]
    public async Task ForceDesktopAsyncOpensDesktopForceInputAppIdUrl()
    {
        Uri? openedUrl = null;
        SteamInputClient client = new((url, _) =>
        {
            openedUrl = url;
            return ValueTask.CompletedTask;
        });

        await client.ForceDesktopAsync().ConfigureAwait(false);

        Assert.AreEqual("steam://forceinputappid/413080", openedUrl?.AbsoluteUri);
    }

    /// <summary>Checks clear URL shape.</summary>
    [TestMethod]
    public async Task ClearAsyncOpensClearForceInputAppIdUrl()
    {
        Uri? openedUrl = null;
        SteamInputClient client = new((url, _) =>
        {
            openedUrl = url;
            return ValueTask.CompletedTask;
        });

        await client.ClearAsync().ConfigureAwait(false);

        Assert.AreEqual("steam://forceinputappid/0", openedUrl?.AbsoluteUri);
    }

    /// <summary>Checks controller configurator URL shape.</summary>
    [TestMethod]
    public async Task OpenControllerConfigAsyncOpensControllerConfigUrl()
    {
        Uri? openedUrl = null;
        SteamInputClient client = new((url, _) =>
        {
            openedUrl = url;
            return ValueTask.CompletedTask;
        });

        await client.OpenControllerConfigAsync(480).ConfigureAwait(false);

        Assert.AreEqual("steam://controllerconfig/480", openedUrl?.AbsoluteUri);
    }

    /// <summary>Checks controller configurator input validation.</summary>
    [TestMethod]
    public async Task OpenControllerConfigAsyncRejectsZeroAppId()
    {
        SteamInputClient client = new();

        _ = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            async () => await client.OpenControllerConfigAsync(0).ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    /// <summary>Checks desktop controller configurator URL shape.</summary>
    [TestMethod]
    public async Task OpenDesktopControllerConfigAsyncOpensDesktopControllerConfigUrl()
    {
        Uri? openedUrl = null;
        SteamInputClient client = new((url, _) =>
        {
            openedUrl = url;
            return ValueTask.CompletedTask;
        });

        await client.OpenDesktopControllerConfigAsync().ConfigureAwait(false);

        Assert.AreEqual("steam://controllerconfig/413080", openedUrl?.AbsoluteUri);
    }

    /// <summary>Checks cancellation before opening a URL.</summary>
    [TestMethod]
    public async Task ForceAsyncHonorsCancellation()
    {
        bool opened = false;
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync().ConfigureAwait(false);
        SteamInputClient client = new((_, _) =>
        {
            opened = true;
            return ValueTask.CompletedTask;
        });

        _ = await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            async () => await client.ForceAsync(123, cancellation.Token).ConfigureAwait(false))
            .ConfigureAwait(false);

        Assert.IsFalse(opened);
    }

    /// <summary>Checks captured URL ordering.</summary>
    [TestMethod]
    public async Task CommandsOpenExpectedUrlsInOrder()
    {
        List<Uri> openedUrls = [];
        SteamInputClient client = new((url, _) =>
        {
            openedUrls.Add(url);
            return ValueTask.CompletedTask;
        });

        await client.ForceAsync(123).ConfigureAwait(false);
        await client.ClearAsync().ConfigureAwait(false);

        Assert.HasCount(2, openedUrls);
        Assert.AreEqual("steam://forceinputappid/123", openedUrls[0].AbsoluteUri);
        Assert.AreEqual("steam://forceinputappid/0", openedUrls[1].AbsoluteUri);
    }
}
