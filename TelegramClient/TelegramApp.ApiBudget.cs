using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace TelegramClient;

public partial class TelegramApp
{
    /// <summary>When set (headless), API rate and download concurrency use one shared budget.</summary>
    public ITelegramApiBudget? ApiBudget { get; set; }

    private async Task AcquireApiAsync(ApiCallKind kind, CancellationToken ct = default)
    {
        if (ApiBudget == null) return;
        await ApiBudget.AcquireAsync(kind, ct).ConfigureAwait(false);
    }

    private void ReportApiFloodWait(Exception ex)
    {
        if (ApiBudget == null || !FloodWaitHelper.TryParseSeconds(ex, out var sec)) return;
        ApiBudget.ReportFloodWait(TimeSpan.FromSeconds(sec));
    }

    private async Task<T> InvokeTelegramApiAsync<T>(
        ApiCallKind kind,
        Func<Task<T>> call,
        CancellationToken ct = default)
    {
        await AcquireApiAsync(kind, ct).ConfigureAwait(false);
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ReportApiFloodWait(ex);
            throw;
        }
    }

    private async Task InvokeTelegramApiAsync(
        ApiCallKind kind,
        Func<Task> call,
        CancellationToken ct = default)
    {
        await AcquireApiAsync(kind, ct).ConfigureAwait(false);
        try
        {
            await call().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ReportApiFloodWait(ex);
            throw;
        }
    }

    private async Task<IDisposable> AcquireDownloadSlotAsync(CancellationToken ct = default)
    {
        if (ApiBudget != null)
            return await ApiBudget.AcquireDownloadSlotAsync(ct).ConfigureAwait(false);

        // Capture once: UpdateConfig may swap _semaphore, and releasing a different instance throws SemaphoreFullException.
        var sem = _semaphore;
        await sem.WaitAsync(ct).ConfigureAwait(false);
        return new SemaphoreSlotRelease(sem);
    }

    private static void ObserveUpdateTask(Task task)
    {
        _ = task.ContinueWith(
            t =>
            {
                if (t.IsFaulted && t.Exception != null)
                    Log.Warning(t.Exception.GetBaseException(), "Live update dispatch failed");
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private sealed class SemaphoreSlotRelease : IDisposable
    {
        private SemaphoreSlim? _sem;

        public SemaphoreSlotRelease(SemaphoreSlim sem) => _sem = sem;

        public void Dispose()
        {
            _sem?.Release();
            _sem = null;
        }
    }
}
