using Microsoft.EntityFrameworkCore;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Common;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.Quizzes;

public sealed record ExamTemplateDto(int SubjectId, int MultipleChoiceCount, int TwoMarkChoiceCount, int EssayCount, double EssayPoints,
    int DurationMinutes, int DiagramCount, double TotalPoints, bool IsDefault, bool IsOfficial);

public sealed record SaveExamTemplateRequest(int MultipleChoiceCount, int TwoMarkChoiceCount, int EssayCount, double EssayPoints, int DurationMinutes, int DiagramCount);

/// <summary>
/// The shape of a Ministry (Thanaweya Amma) exam per subject. The defaults follow the 2026 specifications published by the
/// Ministry of Education for the science subjects (46 questions: 44 multiple choice of which 12 carry two marks, 2 essay
/// questions of 2 marks, 60 marks, 3 hours); other subjects get a general shape. Each student can edit them.
/// </summary>
public sealed class ExamTemplateService(IAppDbContext db, TimeProvider clock)
{
    private static readonly HashSet<string> OfficialSubjects = ["physics", "chemistry", "biology", "geology"];

    public async Task<IReadOnlyList<ExamTemplateDto>> ListAsync(Guid userId, CancellationToken ct)
    {
        var subjects = await db.Subjects.AsNoTracking().OrderBy(s => s.Id).Select(s => new { s.Id, s.Code }).ToListAsync(ct);
        var saved = await db.ExamTemplates.AsNoTracking().Where(t => t.UserId == userId).ToDictionaryAsync(t => t.SubjectId, ct);
        return subjects.Select(s => saved.TryGetValue(s.Id, out var t) ? ToDto(t, s.Code, false) : Default(s.Id, s.Code)).ToList();
    }

    public async Task<ExamTemplateDto> GetEffectiveAsync(Guid userId, int subjectId, CancellationToken ct)
    {
        var code = await db.Subjects.AsNoTracking().Where(s => s.Id == subjectId).Select(s => s.Code).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Subject");
        var saved = await db.ExamTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == userId && t.SubjectId == subjectId, ct);
        return saved is null ? Default(subjectId, code) : ToDto(saved, code, false);
    }

    public async Task<ExamTemplateDto> SaveAsync(Guid userId, int subjectId, SaveExamTemplateRequest r, CancellationToken ct)
    {
        var code = await db.Subjects.AsNoTracking().Where(s => s.Id == subjectId).Select(s => s.Code).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Subject");
        if (r.MultipleChoiceCount is < 0 or > 60 || r.EssayCount is < 0 or > 20 || r.MultipleChoiceCount + r.EssayCount is < 1 or > 60)
            throw new ValidationException("invalid_template", "Use 1–60 questions in total (up to 20 essay questions).");
        if (r.TwoMarkChoiceCount < 0 || r.TwoMarkChoiceCount > r.MultipleChoiceCount || r.DiagramCount < 0 || r.DiagramCount > Math.Min(20, r.MultipleChoiceCount))
            throw new ValidationException("invalid_template", "Two-mark and diagram questions must be part of the multiple-choice questions.");
        if (r.DurationMinutes is < 5 or > 300 || r.EssayPoints is < 0.5 or > 20)
            throw new ValidationException("invalid_template", "Use 5–300 minutes and 0.5–20 marks per essay question.");

        var t = await db.ExamTemplates.FirstOrDefaultAsync(x => x.UserId == userId && x.SubjectId == subjectId, ct);
        if (t is null)
        {
            t = new ExamTemplate { Id = Ids.New(), UserId = userId, SubjectId = subjectId };
            db.ExamTemplates.Add(t);
        }
        t.MultipleChoiceCount = r.MultipleChoiceCount;
        t.TwoMarkChoiceCount = r.TwoMarkChoiceCount;
        t.EssayCount = r.EssayCount;
        t.EssayPoints = r.EssayPoints;
        t.DurationMinutes = r.DurationMinutes;
        t.DiagramCount = r.DiagramCount;
        t.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return ToDto(t, code, false);
    }

    /// <summary>Back to the Ministry default.</summary>
    public async Task<ExamTemplateDto> ResetAsync(Guid userId, int subjectId, CancellationToken ct)
    {
        await db.ExamTemplates.Where(t => t.UserId == userId && t.SubjectId == subjectId).ExecuteDeleteAsync(ct);
        return await GetEffectiveAsync(userId, subjectId, ct);
    }

    internal static ExamTemplateDto Default(int subjectId, string code) => OfficialSubjects.Contains(code)
        ? Build(subjectId, 44, 12, 2, 2, 180, 0, isDefault: true, official: true)
        : Build(subjectId, 40, 0, 4, 5, 180, 0, isDefault: true, official: false);

    private static ExamTemplateDto ToDto(ExamTemplate t, string code, bool isDefault) =>
        Build(t.SubjectId, t.MultipleChoiceCount, t.TwoMarkChoiceCount, t.EssayCount, t.EssayPoints, t.DurationMinutes, t.DiagramCount, isDefault, OfficialSubjects.Contains(code));

    private static ExamTemplateDto Build(int subjectId, int mcq, int twoMark, int essay, double essayPoints, int minutes, int diagrams, bool isDefault, bool official) =>
        new(subjectId, mcq, twoMark, essay, essayPoints, minutes, diagrams, mcq + twoMark + essay * essayPoints, isDefault, official);
}
