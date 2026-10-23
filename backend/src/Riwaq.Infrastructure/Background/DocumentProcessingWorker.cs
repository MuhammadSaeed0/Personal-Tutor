using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Features.Books;
using Riwaq.Application.Ingestion;
using Riwaq.Domain;
using Riwaq.Infrastructure.Persistence;

namespace Riwaq.Infrastructure.Background;

/// <summary>
/// Database-backed job queue for document processing. Jobs survive restarts: on startup any job whose
/// heartbeat went stale is re-queued. Failed jobs are retried with exponential back-off.
/// </summary>
public sealed class DocumentProcessingWorker(
    IServiceScopeFactory scopes,
    IProcessingSignal signal,
    TimeProvider clock,
    ILogger<DocumentProcessingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverStaleJobsAsync(stoppingToken);
        var lastRecovery = clock.GetUtcNow();

        while (!stoppingToken.IsCancellationRequested)
        {
            Guid? jobId = null;
            try
            {
                jobId = await ClaimNextJobAsync(stoppingToken);
                if (jobId is null)
                {
                    // A job interrupted shortly before this start (heartbeat not yet stale) is picked up once it goes stale.
                    if (clock.GetUtcNow() - lastRecovery > TimeSpan.FromMinutes(1))
                    {
                        await RecoverStaleJobsAsync(stoppingToken);
                        lastRecovery = clock.GetUtcNow();
                    }
                    await signal.WaitAsync(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }
                await RunJobAsync(jobId.Value, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Document worker loop error (job {JobId})", jobId);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task RecoverStaleJobsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cutoff = clock.GetUtcNow().UtcDateTime - StaleAfter;
            var recovered = await db.ProcessingJobs
                .Where(j => j.Status == JobStatus.Running && (j.HeartbeatAt == null || j.HeartbeatAt < cutoff))
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, JobStatus.Queued), ct);
            if (recovered > 0) logger.LogInformation("Re-queued {Count} interrupted processing job(s)", recovered);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not recover stale jobs (database not ready?)");
        }
    }

    private async Task<Guid?> ClaimNextJobAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow().UtcDateTime;

        var candidate = await db.ProcessingJobs.AsNoTracking()
            .Where(j => j.Status == JobStatus.Queued && (j.NextAttemptAt == null || j.NextAttemptAt <= now))
            .OrderBy(j => j.CreatedAt)
            .Select(j => (Guid?)j.Id)
            .FirstOrDefaultAsync(ct);
        if (candidate is null) return null;

        // Atomic claim: only one worker instance can move the job from Queued to Running.
        var claimed = await db.ProcessingJobs
            .Where(j => j.Id == candidate && j.Status == JobStatus.Queued)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Running)
                .SetProperty(j => j.StartedAt, now)
                .SetProperty(j => j.HeartbeatAt, now)
                .SetProperty(j => j.Attempt, j => j.Attempt + 1), ct);
        return claimed == 1 ? candidate : null;
    }

    private async Task RunJobAsync(Guid jobId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<DocumentProcessor>();
        try
        {
            await processor.ProcessAsync(jobId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // shutdown: the job stays Running and is recovered on next start
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(jobId, ex, ct);
        }
    }

    private async Task HandleFailureAsync(Guid jobId, Exception ex, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IRealtimeNotifier>();
        var job = await db.ProcessingJobs.FirstAsync(j => j.Id == jobId, ct);
        var book = await db.Books.FirstOrDefaultAsync(b => b.Id == job.BookId, ct);
        if (book is null) return; // deleted while processing

        var permanent = ex is PermanentProcessingException;
        var code = ex switch
        {
            PermanentProcessingException p => p.Code,
            AiUnavailableException a => a.Code,
            _ => "processing_error"
        };
        logger.LogWarning(ex is PermanentProcessingException ? null : ex,
            "Processing job {JobId} failed on attempt {Attempt}: {Code}", jobId, job.Attempt, code);

        var now = clock.GetUtcNow().UtcDateTime;
        if (!permanent && job.Attempt < job.MaxAttempts)
        {
            job.Status = JobStatus.Queued;
            job.NextAttemptAt = now.AddSeconds(30 * Math.Pow(2, job.Attempt - 1));
            job.ErrorMessage = code;
        }
        else
        {
            job.Status = JobStatus.Failed;
            job.CompletedAt = now;
            job.ErrorMessage = code;
            book.Status = BookStatus.Failed;
        }
        book.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await notifier.SendToUserAsync(book.UserId, "processingUpdated", BookMappings.ToStatus(book, job), ct);
    }
}

/// <summary>In-process wake-up signal so newly queued jobs start immediately.</summary>
public sealed class ProcessingSignal : IProcessingSignal
{
    private readonly SemaphoreSlim _semaphore = new(0);

    public void Notify()
    {
        if (_semaphore.CurrentCount == 0) _semaphore.Release();
    }

    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(timeout, cancellationToken);
    }
}
