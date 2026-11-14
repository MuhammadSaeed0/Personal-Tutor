using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Features.StudyPacks;
using Riwaq.Domain.Entities;

namespace Riwaq.Infrastructure.Background;

public sealed class StudyPackSignal : IStudyPackSignal
{
    private readonly SemaphoreSlim _semaphore = new(0);
    public void Notify() { if (_semaphore.CurrentCount == 0) _semaphore.Release(); }
    public Task WaitAsync(TimeSpan timeout, CancellationToken ct) => _semaphore.WaitAsync(timeout, ct);
}

/// <summary>
/// Makes study packs one at a time (each one already makes many AI requests). Packs live in the database, so a pack
/// interrupted by a restart continues from its last finished chapter.
/// </summary>
public sealed class StudyPackWorker(IServiceScopeFactory scopes, StudyPackSignal signal, ILogger<StudyPackWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid? packId = null;
            try
            {
                using (var scope = scopes.CreateScope())
                {
                    var packs = scope.ServiceProvider.GetRequiredService<StudyPackService>();
                    await packs.FailBrokenAsync(stoppingToken);
                    packId = await packs.NextRunnableAsync(stoppingToken);
                }
                if (packId is null)
                {
                    await signal.WaitAsync(TimeSpan.FromSeconds(30), stoppingToken);
                    continue;
                }
                using (var scope = scopes.CreateScope())
                    await scope.ServiceProvider.GetRequiredService<StudyPackService>().RunAsync(packId.Value, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Study pack {PackId} stopped", packId);
                if (packId is not null) await MarkFailedAsync(packId.Value);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task MarkFailedAsync(Guid packId)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            await db.StudyPacks.Where(p => p.Id == packId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, StudyPackStatus.Failed).SetProperty(p => p.ErrorCode, "study_pack_failed"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not mark study pack {PackId} as failed", packId);
        }
    }
}
