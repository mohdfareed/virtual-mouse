using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Inputs;

namespace Hosting;

internal sealed class ControllerRoutePipeServer(
    string pipeName,
    Action<GamepadState> handleInput) : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private NamedPipeServerStream? _pipe;
    private Task? _serverTask;
    private readonly Lock _writeGate = new();

    public string PipeName { get; } = pipeName;

    public void Start()
    {
        _serverTask = Task.Run(RunAsync, CancellationToken.None);
    }

    public void SendFeedback(GamepadRumble rumble)
    {
        NamedPipeServerStream? pipe = _pipe;
        if (pipe is null || !pipe.IsConnected)
        {
            return;
        }

        byte[] buffer = new byte[ControllerRouteFrame.Size];
        ControllerRouteFrame.WriteFeedback(buffer, rumble);

        lock (_writeGate)
        {
            if (pipe.IsConnected)
            {
                pipe.Write(buffer, 0, buffer.Length);
                pipe.Flush();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync().ConfigureAwait(false);
        _pipe?.Dispose();
        if (_serverTask is not null)
        {
            try
            {
                await _serverTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
            }
            catch (IOException) when (_cancellation.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (_cancellation.IsCancellationRequested)
            {
            }
        }

        _cancellation.Dispose();
    }

    private async Task RunAsync()
    {
        using NamedPipeServerStream pipe = new(
            PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        _pipe = pipe;
        await pipe.WaitForConnectionAsync(_cancellation.Token).ConfigureAwait(false);

        byte[] buffer = new byte[ControllerRouteFrame.Size];
        while (!_cancellation.IsCancellationRequested && pipe.IsConnected)
        {
            await ReadExactlyAsync(pipe, buffer, _cancellation.Token).ConfigureAwait(false);
            if (ControllerRouteFrame.GetFrameType(buffer) == ControllerRouteFrameType.Input)
            {
                handleInput(ControllerRouteFrame.ReadInput(buffer));
            }
        }
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream
                .ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Controller route pipe closed.");
            }

            offset += read;
        }
    }
}

internal readonly record struct ControllerRoutePipeInfo(Guid RouteId, string PipeName);

/// <summary>Streams one client-visible controller route to the host.</summary>
public sealed class ControllerRouteClient : IDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly byte[] _buffer = new byte[ControllerRouteFrame.Size];
    private readonly Lock _writeGate = new();

    private ControllerRouteClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
    }

    internal static async Task<ControllerRouteClient> ConnectAsync(
        ControllerRoutePipeInfo route,
        CancellationToken cancellationToken)
    {
        NamedPipeClientStream pipe = new(
            ".",
            route.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return new ControllerRouteClient(pipe);
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Sends one controller input update.</summary>
    public void Send(GamepadInput input)
    {
        ControllerRouteFrame.WriteInput(_buffer, input.State);
        lock (_writeGate)
        {
            _pipe.Write(_buffer, 0, _buffer.Length);
            _pipe.Flush();
        }
    }

    /// <summary>Runs the route-local feedback loop until cancelled.</summary>
    public void RunFeedback(Action<GamepadRumble> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);

        byte[] buffer = new byte[ControllerRouteFrame.Size];
        while (!cancellationToken.IsCancellationRequested)
        {
            ReadExactly(_pipe, buffer, cancellationToken);
            if (ControllerRouteFrame.GetFrameType(buffer) == ControllerRouteFrameType.Feedback)
            {
                handler(ControllerRouteFrame.ReadFeedback(buffer));
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _pipe.Dispose();
    }

    private static void ReadExactly(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
            {
                throw new EndOfStreamException("Controller route pipe closed.");
            }

            offset += read;
        }
    }
}

internal enum ControllerRouteFrameType : byte
{
    Input = 1,
    Feedback = 2,
}

internal static class ControllerRouteFrame
{
    private const int FrameTypeOffset = 0;
    private const int ButtonsOffset = 1;
    private const int LeftXOffset = 5;
    private const int LeftYOffset = 7;
    private const int RightXOffset = 9;
    private const int RightYOffset = 11;
    private const int LeftTriggerOffset = 13;
    private const int RightTriggerOffset = 15;
    private const int HasGyroOffset = 17;
    private const int GyroXOffset = 18;
    private const int GyroYOffset = 22;
    private const int GyroZOffset = 26;
    private const int HasAccelerometerOffset = 30;
    private const int AccelXOffset = 31;
    private const int AccelYOffset = 35;
    private const int AccelZOffset = 39;
    private const int RumbleLowOffset = 43;
    private const int RumbleHighOffset = 45;

    public const int Size = 47;

    public static ControllerRouteFrameType GetFrameType(ReadOnlySpan<byte> buffer)
    {
        ValidateBuffer(buffer);
        return (ControllerRouteFrameType)buffer[FrameTypeOffset];
    }

    public static void WriteInput(Span<byte> buffer, GamepadState state)
    {
        ValidateBuffer(buffer);
        buffer.Clear();
        buffer[FrameTypeOffset] = (byte)ControllerRouteFrameType.Input;
        WriteUInt32(buffer, ButtonsOffset, (uint)state.Buttons);
        WriteInt16(buffer, LeftXOffset, state.LeftX);
        WriteInt16(buffer, LeftYOffset, state.LeftY);
        WriteInt16(buffer, RightXOffset, state.RightX);
        WriteInt16(buffer, RightYOffset, state.RightY);
        WriteUInt16(buffer, LeftTriggerOffset, state.LeftTrigger);
        WriteUInt16(buffer, RightTriggerOffset, state.RightTrigger);
        buffer[HasGyroOffset] = state.Motion.HasGyro ? (byte)1 : (byte)0;
        WriteSingle(buffer, GyroXOffset, state.Motion.GyroX);
        WriteSingle(buffer, GyroYOffset, state.Motion.GyroY);
        WriteSingle(buffer, GyroZOffset, state.Motion.GyroZ);
        buffer[HasAccelerometerOffset] = state.Motion.HasAccelerometer ? (byte)1 : (byte)0;
        WriteSingle(buffer, AccelXOffset, state.Motion.AccelX);
        WriteSingle(buffer, AccelYOffset, state.Motion.AccelY);
        WriteSingle(buffer, AccelZOffset, state.Motion.AccelZ);
    }

    public static GamepadState ReadInput(ReadOnlySpan<byte> buffer)
    {
        ValidateBuffer(buffer);
        return new GamepadState(
            (GamepadButtons)ReadUInt32(buffer, ButtonsOffset),
            ReadInt16(buffer, LeftXOffset),
            ReadInt16(buffer, LeftYOffset),
            ReadInt16(buffer, RightXOffset),
            ReadInt16(buffer, RightYOffset),
            ReadUInt16(buffer, LeftTriggerOffset),
            ReadUInt16(buffer, RightTriggerOffset),
            new GamepadMotion(
                buffer[HasGyroOffset] != 0,
                ReadSingle(buffer, GyroXOffset),
                ReadSingle(buffer, GyroYOffset),
                ReadSingle(buffer, GyroZOffset),
                buffer[HasAccelerometerOffset] != 0,
                ReadSingle(buffer, AccelXOffset),
                ReadSingle(buffer, AccelYOffset),
                ReadSingle(buffer, AccelZOffset)));
    }

    public static void WriteFeedback(Span<byte> buffer, GamepadRumble rumble)
    {
        ValidateBuffer(buffer);
        buffer.Clear();
        buffer[FrameTypeOffset] = (byte)ControllerRouteFrameType.Feedback;
        WriteUInt16(buffer, RumbleLowOffset, rumble.LowFrequency);
        WriteUInt16(buffer, RumbleHighOffset, rumble.HighFrequency);
    }

    public static GamepadRumble ReadFeedback(ReadOnlySpan<byte> buffer)
    {
        ValidateBuffer(buffer);
        return new GamepadRumble(
            ReadUInt16(buffer, RumbleLowOffset),
            ReadUInt16(buffer, RumbleHighOffset));
    }

    private static void ValidateBuffer(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < Size)
        {
            throw new ArgumentException("Controller route frame buffer is too small.", nameof(buffer));
        }
    }

    private static void WriteUInt32(Span<byte> buffer, int offset, uint value)
    {
        _ = BitConverter.TryWriteBytes(buffer.Slice(offset, sizeof(uint)), value);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> buffer, int offset)
    {
        return BitConverter.ToUInt32(buffer.Slice(offset, sizeof(uint)));
    }

    private static void WriteUInt16(Span<byte> buffer, int offset, ushort value)
    {
        _ = BitConverter.TryWriteBytes(buffer.Slice(offset, sizeof(ushort)), value);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> buffer, int offset)
    {
        return BitConverter.ToUInt16(buffer.Slice(offset, sizeof(ushort)));
    }

    private static void WriteInt16(Span<byte> buffer, int offset, short value)
    {
        _ = BitConverter.TryWriteBytes(buffer.Slice(offset, sizeof(short)), value);
    }

    private static short ReadInt16(ReadOnlySpan<byte> buffer, int offset)
    {
        return BitConverter.ToInt16(buffer.Slice(offset, sizeof(short)));
    }

    private static void WriteSingle(Span<byte> buffer, int offset, float value)
    {
        _ = BitConverter.TryWriteBytes(buffer.Slice(offset, sizeof(float)), value);
    }

    private static float ReadSingle(ReadOnlySpan<byte> buffer, int offset)
    {
        return BitConverter.ToSingle(buffer.Slice(offset, sizeof(float)));
    }
}
