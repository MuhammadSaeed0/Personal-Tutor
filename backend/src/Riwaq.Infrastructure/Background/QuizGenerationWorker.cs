using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Features.Quizzes;
using Riwaq.Domain;

namespace Riwaq.Infrastructure.Background;

/// <summary>In-process queue of quizzes waiting for their questions. The database stays the source of truth.</summary>
public sealed class QuizGenerationQueue : IQuizGenerationQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();
    public void Enqueue(Guid quizId) => _channel.Writer.TryWrite(quizId);
    internal ChannelReader<Guid> Reader => _channel.Reader;
}

/// <summary>
/// Writes the questions of queued quizzes, at most two at a time so Claude is not flooded with parallel requests.
/// On start every quiz still marked Generating is queued again, so a restart never loses one.
/// </summary>
public sealed class QuizGenerationWorker(
    IServiceScopeFactory scopes,
    QuizGenerationQueue queue,
    ILogger<QuizGenerationWorker> logger) : BackgroundService
{
    private const int MaxParallel = 2;
    private readonly SemaphoreSlim _slots = new(MaxParallel);
    private readonly ConcurrentDictionary<Guid, byte> _inFlight = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using (var scope = scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                var pending = await db.Quizzes.AsNoTracking().Where(q => q.Status == QuizStatus.Generating)
                    .OrderBy(q => q.CreatedAt).Select(q => q.Id).ToListAsync(stoppingToken);
                foreach (var id in pending) queue.Enqueue(id);
            }

            await foreach (var quizId in queue.Reader.ReadAllAsync(stoppingToken))
            {
                if (!_inFlight.TryAdd(quizId, 0)) continue; // already being written
                await _slots.WaitAsync(stoppingToken);
                _ = Task.Run(() => RunAsync(quizId, stoppingToken), CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task RunAsync(Guid quizId, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<QuizService>().RunGenerationAsync(quizId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Quiz worker error (quiz {QuizId})", quizId);
        }
        finally
        {
            _inFlight.TryRemove(quizId, out _);
            _slots.Release();
        }
    }
}
