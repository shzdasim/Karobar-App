namespace Karobar.Host;

/// <summary>
/// Coordinates atomic ref-counted deferred disposal of CancellationTokenSource and SemaphoreSlim
/// gates so Dispose() cancels all in-flight operations immediately without disposing gates while
/// any WaitAsync continuation, semaphore release, or linked CTS is still unwinding.
/// </summary>
public sealed class DeferredSemaphoreDisposalCoordinator : IDisposable
{
    private readonly IDisposable[] _managedResources;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _activeOps = 1; // Baseline 1 = coordinator alive (decremented once by Dispose)
    private int _disposeInitiated;
    private int _resourcesDisposed;

    public DeferredSemaphoreDisposalCoordinator(params IDisposable[] managedResources)
    {
        _managedResources = managedResources ?? Array.Empty<IDisposable>();
    }

    public bool IsDisposed => Volatile.Read(ref _disposeInitiated) != 0;

    public bool AreResourcesDisposed => Volatile.Read(ref _resourcesDisposed) != 0;

    public bool TryEnterOperation(CancellationToken externalToken, out CancellationTokenSource? linkedCts)
    {
        linkedCts = null;

        while (true)
        {
            var current = Volatile.Read(ref _activeOps);
            if (current <= 0 || Volatile.Read(ref _disposeInitiated) != 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _activeOps, current + 1, current) == current)
            {
                break;
            }
        }

        // _activeOps >= 1 until ExitOperation() runs, so _disposeCts cannot be disposed here.
        try
        {
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken, _disposeCts.Token);
            if (Volatile.Read(ref _disposeInitiated) != 0)
            {
                linkedCts.Dispose();
                linkedCts = null;
                ExitOperation();
                return false;
            }

            return true;
        }
        catch
        {
            linkedCts?.Dispose();
            linkedCts = null;
            ExitOperation();
            throw;
        }
    }

    public void ExitOperation()
    {
        if (Interlocked.Decrement(ref _activeOps) == 0)
        {
            DisposeManagedResources();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeInitiated, 1) != 0)
        {
            return;
        }

        try
        {
            _disposeCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        ExitOperation();
    }

    private void DisposeManagedResources()
    {
        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
        {
            return;
        }

        foreach (var resource in _managedResources)
        {
            try
            {
                resource.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        try
        {
            _disposeCts.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
