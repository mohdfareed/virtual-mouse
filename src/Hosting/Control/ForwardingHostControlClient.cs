using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hosting;

/// <summary>Status reported by the local forwarding host.</summary>
/// <param name="RouteId">Hosted route id.</param>
/// <param name="IsEnabled">Whether forwarding is enabled.</param>
/// <param name="IsConnected">Whether route input and output are connected.</param>
/// <param name="EnabledClientCount">Number of connected enabled clients.</param>
public readonly record struct ForwardingHostStatus(
    string RouteId,
    bool IsEnabled,
    bool IsConnected,
    int EnabledClientCount);

/// <summary>Controls a local forwarding host.</summary>
public sealed class ForwardingHostControlClient(
    string pipeName = ForwardingHostControlServer.DefaultPipeName,
    TimeSpan? connectTimeout = null)
{
    private static readonly Encoding PipeEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Enables forwarding until the returned lease is disposed.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ForwardingHostEnableLease> EnableAsync(CancellationToken cancellationToken = default)
    {
        NamedPipeClientStream pipe = CreatePipe();
        try
        {
            await ConnectAsync(pipe, cancellationToken).ConfigureAwait(false);
            StreamWriter writer = new(pipe, PipeEncoding, leaveOpen: true)
            {
                NewLine = "\n",
                AutoFlush = true,
            };
            StreamReader reader = new(pipe, PipeEncoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

            await writer.WriteLineAsync("ENABLE".AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            string response = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) ??
                throw new IOException("Host closed the control pipe without a response.");

            return response.StartsWith("ERR ", StringComparison.Ordinal)
                ? throw new InvalidOperationException(response[4..])
                : response == "OK enabled"
                ? new ForwardingHostEnableLease(pipe, reader, writer)
                : throw new IOException("Host returned an invalid enable response.");
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Gets host status.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ForwardingHostStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        string response = await SendCommandAsync("STATUS", cancellationToken).ConfigureAwait(false);
        return ForwardingHostControlProtocol.ParseStatus(response);
    }

    private async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        using NamedPipeClientStream pipe = CreatePipe();

        await ConnectAsync(pipe, cancellationToken).ConfigureAwait(false);
        using StreamWriter writer = new(pipe, PipeEncoding, leaveOpen: true)
        {
            NewLine = "\n",
            AutoFlush = true,
        };
        using StreamReader reader = new(pipe, PipeEncoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

        await writer.WriteLineAsync(command.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        string response = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) ??
            throw new IOException("Host closed the control pipe without a response.");

        return response.StartsWith("ERR ", StringComparison.Ordinal)
            ? throw new InvalidOperationException(response[4..])
            : response;
    }

    private NamedPipeClientStream CreatePipe()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        return new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
    }

    private async Task ConnectAsync(NamedPipeClientStream pipe, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(connectTimeout ?? DefaultConnectTimeout);

        try
        {
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out connecting to the host control pipe.");
        }
    }
}

/// <summary>Keeps host forwarding enabled while connected.</summary>
public sealed class ForwardingHostEnableLease : IAsyncDisposable, IDisposable
{
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    internal ForwardingHostEnableLease(
        NamedPipeClientStream pipe,
        StreamReader reader,
        StreamWriter writer)
    {
        _pipe = pipe;
        _reader = reader;
        _writer = writer;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        NamedPipeClientStream? pipe = Interlocked.Exchange(ref _pipe, null);
        StreamReader? reader = Interlocked.Exchange(ref _reader, null);
        StreamWriter? writer = Interlocked.Exchange(ref _writer, null);
        reader?.Dispose();
        writer?.Dispose();
        pipe?.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        NamedPipeClientStream? pipe = Interlocked.Exchange(ref _pipe, null);
        StreamReader? reader = Interlocked.Exchange(ref _reader, null);
        StreamWriter? writer = Interlocked.Exchange(ref _writer, null);
        reader?.Dispose();
        if (writer is not null)
        {
            await writer.DisposeAsync().ConfigureAwait(false);
        }

        if (pipe is not null)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }
}
