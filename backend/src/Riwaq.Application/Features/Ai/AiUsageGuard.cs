using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Riwaq.Application.Abstractions;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.Ai;

public sealed record AiUsageDayDto(DateOnly Date, int Requests, long InputTokens, long OutputTokens);
public sealed record AiUsageFeatureDto(string Feature, int Requests, int Failed, long InputTokens, long OutputTokens);
public sealed record AiUsageDto(int Days, IReadOnlyList<AiUsageDayDto> Daily, IReadOnlyList<AiUsageFeatureDto> ByFeature,
    int TodayRequests, long TodayOutputTokens, int DailyRequestLimit, int DailyOutputTokenLimit);

/// <summary>Per-student daily AI limits and usage accounting.</summary>
public sealed class AiUsageGuard(IAppDbContext db, IConfiguration configuration, TimeProvider clock)
{
    private int DailyRequests => configuration.GetValue("Ai:Limits:DailyRequestsPerUser", 300);
    private int DailyOutputTokens => configuration.GetValue("Ai:Limits:DailyOutputTokensPerUser", 600_000);

    public async Task EnsureAllowedAsync(Guid userId, CancellationToken ct)
    {
        var since = clock.GetUtcNow().UtcDateTime.Date;
        var usage = await db.AiUsage.Where(u => u.UserId == userId && u.CreatedAt >= since)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Output = g.Sum(u => u.OutputTokens) })
            .FirstOrDefaultAsync(ct);
        if (usage is not null && (usage.Count >= DailyRequests || usage.Output >= DailyOutputTokens))
            throw new AiUnavailableException("ai_quota_exceeded", "You have reached today's AI usage limit. It resets at midnight UTC.");
    }

    /// <summary>The student's own AI use over the last days, per day and per feature, with today's usage against the limits.</summary>
    public async Task<AiUsageDto> GetUsageAsync(Guid userId, int days, CancellationToken ct)
    {
        days = Math.Clamp(days, 1, 90);
        var today = clock.GetUtcNow().UtcDateTime.Date;
        var since = today.AddDays(1 - days);
        var rows = db.AiUsage.AsNoTracking().Where(u => u.UserId == userId && u.CreatedAt >= since);

        var daily = await rows.GroupBy(u => u.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Requests = g.Count(), Input = g.Sum(u => (long)u.InputTokens), Output = g.Sum(u => (long)u.OutputTokens) })
            .ToListAsync(ct);
        var byFeature = await rows.GroupBy(u => u.Feature)
            .Select(g => new AiUsageFeatureDto(g.Key, g.Count(), g.Count(u => !u.Success), g.Sum(u => (long)u.InputTokens), g.Sum(u => (long)u.OutputTokens)))
            .ToListAsync(ct);

        var series = Enumerable.Range(0, days).Select(i =>
        {
            var day = since.AddDays(i);
            var d = daily.FirstOrDefault(x => x.Day == day);
            return new AiUsageDayDto(DateOnly.FromDateTime(day), d?.Requests ?? 0, d?.Input ?? 0, d?.Output ?? 0);
        }).ToList();
        var todayRow = series[^1];
        return new AiUsageDto(days, series, byFeature.OrderByDescending(f => f.Requests).ToList(),
            todayRow.Requests, todayRow.OutputTokens, DailyRequests, DailyOutputTokens);
    }

    public void Record(Guid userId, string feature, ChatResult? result, string model, bool success)
    {
        db.AiUsage.Add(new AiUsageRecord
        {
            UserId = userId,
            Feature = feature,
            Model = result?.Model ?? model,
            InputTokens = result?.InputTokens ?? 0,
            OutputTokens = result?.OutputTokens ?? 0,
            Success = success,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        });
    }
}
