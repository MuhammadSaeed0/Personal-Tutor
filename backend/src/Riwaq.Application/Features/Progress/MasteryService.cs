using Microsoft.EntityFrameworkCore;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Common;
using Riwaq.Application.Learning;
using Riwaq.Application.Text;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.Progress;

public enum EvidenceKind
{
    Question,
    Card
}

/// <summary>Records assessment evidence into chapter-level and concept-level mastery records.</summary>
public sealed class MasteryService(IAppDbContext db, TimeProvider clock)
{
    public async Task RecordAsync(Guid userId, int subjectId, Guid? bookId, Guid? chapterId, string? topic, EvidenceKind kind,
        double fraction, int? sourcePage, CancellationToken ct)
    {
        string? chapterTitle = null;
        if (chapterId is not null)
        {
            chapterTitle = await db.Chapters.AsNoTracking().Where(c => c.Id == chapterId).Select(c => c.Title).FirstOrDefaultAsync(ct);
            await ApplyAsync(userId, subjectId, bookId, chapterId, $"ch:{chapterId}", chapterTitle ?? "Chapter", kind, fraction, sourcePage, ct);
        }

        if (!string.IsNullOrWhiteSpace(topic))
        {
            var key = $"t:{subjectId}:{Normalize(topic)}";
            await ApplyAsync(userId, subjectId, bookId, chapterId, key, topic.Trim(), kind, fraction, sourcePage, ct);
        }
        else if (chapterId is null)
        {
            await ApplyAsync(userId, subjectId, bookId, null, $"s:{subjectId}:{bookId}", "General", kind, fraction, sourcePage, ct);
        }
    }

    private async Task ApplyAsync(Guid userId, int subjectId, Guid? bookId, Guid? chapterId, string key, string label, EvidenceKind kind,
        double fraction, int? sourcePage, CancellationToken ct)
    {
        if (key.Length > 200) key = key[..200];
        var record = db.TopicMastery.Local.FirstOrDefault(r => r.UserId == userId && r.TopicKey == key)
                     ?? await db.TopicMastery.FirstOrDefaultAsync(r => r.UserId == userId && r.TopicKey == key, ct);
        if (record is null)
        {
            record = new TopicMasteryRecord
            {
                Id = Ids.New(), UserId = userId, SubjectId = subjectId, BookId = bookId, ChapterId = chapterId, TopicKey = key,
                TopicLabel = label.Length > 300 ? label[..300] : label
            };
            db.TopicMastery.Add(record);
        }

        var evidence = record.QuestionAttempts + record.CardReviews;
        var success = fraction >= 0.999;
        if (kind == EvidenceKind.Question)
        {
            record.QuestionAttempts++;
            if (success) record.QuestionCorrect++;
            record.EstimatedMastery = MasteryModel.Update(record.EstimatedMastery, evidence, fraction, MasteryModel.QuestionWeight);
        }
        else
        {
            record.CardReviews++;
            if (!success) record.CardLapses++;
            record.EstimatedMastery = MasteryModel.Update(record.EstimatedMastery, evidence, success, MasteryModel.CardWeight);
        }
        if (!success && sourcePage is not null) record.LastMistakePage = sourcePage;
        record.UpdatedAt = clock.GetUtcNow().UtcDateTime;
    }

    private static string Normalize(string topic) =>
        string.Join(' ', SearchTokenizer.Tokenize(topic)).Trim() is { Length: > 0 } s ? s : ArabicText.FoldForMatching(topic).Trim();
}
