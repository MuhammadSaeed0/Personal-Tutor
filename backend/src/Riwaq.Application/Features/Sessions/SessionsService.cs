using Microsoft.EntityFrameworkCore;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Common;
using Riwaq.Domain;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.Sessions;

public sealed record SessionDto(Guid Id, StudySessionType Type, int? SubjectId, Guid? BookId, Guid? ChapterId, string? Goal, string? Notes,
    DateTime StartedAt, DateTime? EndedAt, int DurationSeconds, int PlannedMinutes);

public sealed record StartSessionRequest(int? SubjectId, Guid? BookId, Guid? ChapterId, string? Goal, int PlannedMinutes);

public sealed record CompleteSessionRequest(int FocusedSeconds, string? Notes);

public sealed record NotificationDto(Guid Id, string Type, string? EntityId, string? Data, bool IsRead, DateTime CreatedAt);

/// <summary>Focus-mode sessions (Pomodoro or custom). Reading time is recorded automatically by the reader.</summary>
public sealed class SessionsService(IAppDbContext db, TimeProvider clock)
{
    public async Task<SessionDto> StartAsync(Guid userId, StartSessionRequest request, CancellationToken ct)
    {
        int? subjectId = request.SubjectId;
        if (request.BookId is not null)
        {
            var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BookId && b.UserId == userId, ct) ?? throw new NotFoundException("Book");
            subjectId ??= book.SubjectId;
        }
        if (subjectId is not null && !await db.Subjects.AnyAsync(s => s.Id == subjectId, ct)) throw new ValidationException("invalid_subject", "Unknown subject.");

        // Close any abandoned focus session first so it doesn't count forever.
        var stale = await db.StudySessions.Where(s => s.UserId == userId && s.Type == StudySessionType.Focus && s.EndedAt == null).ToListAsync(ct);
        foreach (var s in stale) s.EndedAt = s.StartedAt.AddSeconds(s.DurationSeconds);

        var session = new StudySession
        {
            Id = Ids.New(), UserId = userId, Type = StudySessionType.Focus, SubjectId = subjectId, BookId = request.BookId,
            ChapterId = request.ChapterId, Goal = Trim(request.Goal, 500), PlannedMinutes = Math.Clamp(request.PlannedMinutes, 1, 240),
            StartedAt = clock.GetUtcNow().UtcDateTime
        };
        db.StudySessions.Add(session);
        await db.SaveChangesAsync(ct);
        return ToDto(session);
    }

    /// <summary>Completes a focus session. Focused time (excluding pauses) is measured by the client and capped by wall-clock time.</summary>
    public async Task<SessionDto> CompleteAsync(Guid userId, Guid id, CompleteSessionRequest request, CancellationToken ct)
    {
        var session = await db.StudySessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId && s.Type == StudySessionType.Focus, ct)
                      ?? throw new NotFoundException("Session");
        var now = clock.GetUtcNow().UtcDateTime;
        var wall = (int)(now - session.StartedAt).TotalSeconds;
        session.DurationSeconds = Math.Clamp(request.FocusedSeconds, 0, Math.Max(0, wall) + 5);
        session.Notes = Trim(request.Notes, 4000);
        session.EndedAt = now;
        await db.SaveChangesAsync(ct);
        return ToDto(session);
    }

    public async Task<PagedResult<SessionDto>> ListAsync(Guid userId, StudySessionType? type, int page, int pageSize, CancellationToken ct)
    {
        var query = db.StudySessions.AsNoTracking().Where(s => s.UserId == userId && s.EndedAt != null && s.DurationSeconds >= 30);
        if (type is not null) query = query.Where(s => s.Type == type);
        var paged = await query.OrderByDescending(s => s.StartedAt).ToPagedAsync(page, pageSize, ct);
        return new PagedResult<SessionDto>(paged.Items.Select(ToDto).ToList(), paged.Page, paged.PageSize, paged.TotalCount);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var deleted = await db.StudySessions.Where(s => s.Id == id && s.UserId == userId).ExecuteDeleteAsync(ct);
        if (deleted == 0) throw new NotFoundException("Session");
    }

    public async Task<IReadOnlyList<NotificationDto>> GetNotificationsAsync(Guid userId, CancellationToken ct) =>
        await db.Notifications.AsNoTracking().Where(n => n.UserId == userId).OrderByDescending(n => n.CreatedAt).Take(30)
            .Select(n => new NotificationDto(n.Id, n.Type, n.EntityId, n.Data, n.IsRead, n.CreatedAt)).ToListAsync(ct);

    public async Task MarkNotificationsReadAsync(Guid userId, CancellationToken ct) =>
        await db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);

    private static string? Trim(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > max ? s[..max] : s;
    }

    public static SessionDto ToDto(StudySession s) => new(s.Id, s.Type, s.SubjectId, s.BookId, s.ChapterId, s.Goal, s.Notes, s.StartedAt, s.EndedAt, s.DurationSeconds, s.PlannedMinutes);
}
