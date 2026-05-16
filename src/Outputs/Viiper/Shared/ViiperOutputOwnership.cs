using System;
using System.Threading;

namespace Outputs.Viiper;

internal sealed class ViiperOutputOwnership : IDisposable
{
    private Semaphore? _semaphore;

    private ViiperOutputOwnership(Semaphore semaphore)
    {
        _semaphore = semaphore;
    }

    public static ViiperOutputOwnership? TryAcquire(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Semaphore semaphore = new(initialCount: 1, maximumCount: 1, name);

        try
        {
            return semaphore.WaitOne(0)
                ? new ViiperOutputOwnership(semaphore)
                : DisposeSemaphore(semaphore);
        }
        catch
        {
            semaphore.Dispose();
            throw;
        }
    }

    public static ViiperOutputOwnership AcquireOrThrow(string name, string message)
    {
        return TryAcquire(name) ?? throw new InvalidOperationException(message);
    }

    public void Dispose()
    {
        Semaphore? semaphore = Interlocked.Exchange(ref _semaphore, null);
        if (semaphore is null)
        {
            return;
        }

        try
        {
            _ = semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
        }
        finally
        {
            semaphore.Dispose();
        }
    }

    private static ViiperOutputOwnership? DisposeSemaphore(Semaphore semaphore)
    {
        semaphore.Dispose();
        return null;
    }
}
