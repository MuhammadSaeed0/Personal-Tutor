using Microsoft.EntityFrameworkCore;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Common;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.Profile;

public sealed record PreferencesDto(
    string UiLanguage, string Theme, string ExplanationLanguage, bool BilingualTerms, string TeachingStyle,
    bool MotivationEnabled, bool NotificationsEnabled, int FocusMinutes, int BreakMinutes, int WeeklyGoalMinutes);

public sealed record AcademicProfileDto(
    string? Grade, string? Curriculum, string? AcademicYear, int? TargetScore, int DailyStudyMinutes,
    IReadOnlyList<int> DifficultSubjectIds, bool OnboardingCompleted);

public sealed record ProfileDto(Guid Id, string Email, string DisplayName, DateTime CreatedAt, PreferencesDto Preferences, AcademicProfileDto Academic);

public sealed record UpdateProfileRequest(string? DisplayName);

public sealed record UpdatePreferencesRequest(
    string? UiLanguage, string? Theme, string? ExplanationLanguage, bool? BilingualTerms, string? TeachingStyle,
    bool? MotivationEnabled, bool? NotificationsEnabled, int? FocusMinutes, int? BreakMinutes, int? WeeklyGoalMinutes);

public sealed record OnboardingRequest(
    string? Grade, string? Curriculum, string? AcademicYear, int? TargetScore, int? DailyStudyMinutes,
    IReadOnlyList<int>? DifficultSubjectIds, IReadOnlyList<ExamInput>? Exams, bool Completed = true);

public sealed record UpdateAcademicRequest(
    string? Grade, string? Curriculum, string? AcademicYear, int? TargetScore, int? DailyStudyMinutes, IReadOnlyList<int>? DifficultSubjectIds);

public sealed record ExamInput(string Title, DateOnly Date, int? SubjectId, string? Notes);

public sealed record ExamDto(Guid Id, string Title, DateOnly Date, int? SubjectId, string? Notes, int DaysLeft);

public sealed record SubjectDto(int Id, string Code, string NameAr, string NameEn, string Color, string Icon, int SortOrder, string ContentLanguage);

public sealed class ProfileService(IAppDbContext db, TimeProvider clock)
{
    private static readonly string[] Languages = ["ar", "en"];
    private static readonly string[] ExplanationLanguages = ["ar", "en", "fr"];
    private static readonly string[] Themes = ["light", "dark", "system"];
    private static readonly string[] TeachingStyles = ["guided", "concise"];

    public async Task<ProfileDto> GetAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Include(u => u.Preferences).Include(u => u.AcademicProfile)
            .FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("User");
        return ToDto(user, user.Preferences ?? new UserPreferences(), user.AcademicProfile ?? new AcademicProfile());
    }

    public async Task<ProfileDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("User");
        if (request.DisplayName is not null)
        {
            var name = request.DisplayName.Trim();
            if (name.Length is < 1 or > 100) throw new ValidationException("invalid_name", "Display name must be 1–100 characters.");
            user.DisplayName = name;
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(userId, ct);
    }

    public async Task<PreferencesDto> UpdatePreferencesAsync(Guid userId, UpdatePreferencesRequest r, CancellationToken ct)
    {
        var prefs = await GetOrCreatePreferencesAsync(userId, ct);
        if (r.UiLanguage is not null) prefs.UiLanguage = OneOf(r.UiLanguage, Languages, "uiLanguage");
        if (r.Theme is not null) prefs.Theme = OneOf(r.Theme, Themes, "theme");
        if (r.ExplanationLanguage is not null) prefs.ExplanationLanguage = OneOf(r.ExplanationLanguage, ExplanationLanguages, "explanationLanguage");
        if (r.TeachingStyle is not null) prefs.TeachingStyle = OneOf(r.TeachingStyle, TeachingStyles, "teachingStyle");
        if (r.BilingualTerms is not null) prefs.BilingualTerms = r.BilingualTerms.Value;
        if (r.MotivationEnabled is not null) prefs.MotivationEnabled = r.MotivationEnabled.Value;
        if (r.NotificationsEnabled is not null) prefs.NotificationsEnabled = r.NotificationsEnabled.Value;
        if (r.FocusMinutes is not null) prefs.FocusMinutes = Math.Clamp(r.FocusMinutes.Value, 5, 180);
        if (r.BreakMinutes is not null) prefs.BreakMinutes = Math.Clamp(r.BreakMinutes.Value, 1, 60);
        if (r.WeeklyGoalMinutes is not null) prefs.WeeklyGoalMinutes = Math.Clamp(r.WeeklyGoalMinutes.Value, 30, 6000);
        await db.SaveChangesAsync(ct);
        return ToDto(prefs);
    }

    public async Task<ProfileDto> CompleteOnboardingAsync(Guid userId, OnboardingRequest r, CancellationToken ct)
    {
        var profile = await db.AcademicProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null)
        {
            profile = new AcademicProfile { UserId = userId };
            db.AcademicProfiles.Add(profile);
        }

        profile.Grade = Trim(r.Grade, 60);
        profile.Curriculum = Trim(r.Curriculum, 120);
        profile.AcademicYear = Trim(r.AcademicYear, 20);
        profile.TargetScore = r.TargetScore is null ? null : Math.Clamp(r.TargetScore.Value, 0, 100);
        if (r.DailyStudyMinutes is not null) profile.DailyStudyMinutes = Math.Clamp(r.DailyStudyMinutes.Value, 15, 720);
        if (r.DifficultSubjectIds is not null)
        {
            var valid = await db.Subjects.Select(s => s.Id).ToListAsync(ct);
            profile.DifficultSubjectIds = r.DifficultSubjectIds.Where(valid.Contains).Distinct().ToList();
        }
        profile.OnboardingCompleted = r.Completed;

        if (r.Exams is not null)
        {
            foreach (var exam in r.Exams.Take(20)) await AddExamInternalAsync(userId, exam, ct);
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(userId, ct);
    }

    /// <summary>Partial update of the academic profile; only supplied fields change.</summary>
    public async Task<ProfileDto> UpdateAcademicAsync(Guid userId, UpdateAcademicRequest r, CancellationToken ct)
    {
        var profile = await db.AcademicProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null)
        {
            profile = new AcademicProfile { UserId = userId, OnboardingCompleted = true };
            db.AcademicProfiles.Add(profile);
        }
        if (r.Grade is not null) profile.Grade = Trim(r.Grade, 60);
        if (r.Curriculum is not null) profile.Curriculum = Trim(r.Curriculum, 120);
        if (r.AcademicYear is not null) profile.AcademicYear = Trim(r.AcademicYear, 20);
        if (r.TargetScore is not null) profile.TargetScore = Math.Clamp(r.TargetScore.Value, 0, 100);
        if (r.DailyStudyMinutes is not null) profile.DailyStudyMinutes = Math.Clamp(r.DailyStudyMinutes.Value, 15, 720);
        if (r.DifficultSubjectIds is not null)
        {
            var valid = await db.Subjects.Select(s => s.Id).ToListAsync(ct);
            profile.DifficultSubjectIds = r.DifficultSubjectIds.Where(valid.Contains).Distinct().ToList();
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(userId, ct);
    }

    public async Task<IReadOnlyList<SubjectDto>> GetSubjectsAsync(CancellationToken ct) =>
        await db.Subjects.AsNoTracking().OrderBy(s => s.SortOrder)
            .Select(s => new SubjectDto(s.Id, s.Code, s.NameAr, s.NameEn, s.Color, s.Icon, s.SortOrder, s.ContentLanguage))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ExamDto>> GetExamsAsync(Guid userId, bool upcomingOnly, CancellationToken ct)
    {
        var today = Today();
        var query = db.Exams.AsNoTracking().Where(e => e.UserId == userId);
        if (upcomingOnly) query = query.Where(e => e.Date >= today);
        var exams = await query.OrderBy(e => e.Date).ToListAsync(ct);
        return exams.Select(e => ToDto(e, today)).ToList();
    }

    public async Task<ExamDto> AddExamAsync(Guid userId, ExamInput input, CancellationToken ct)
    {
        var exam = await AddExamInternalAsync(userId, input, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(exam, Today());
    }

    public async Task<ExamDto> UpdateExamAsync(Guid userId, Guid examId, ExamInput input, CancellationToken ct)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.Id == examId && e.UserId == userId, ct) ?? throw new NotFoundException("Exam");
        await ValidateExamAsync(input, ct);
        exam.Title = input.Title.Trim();
        exam.Date = input.Date;
        exam.SubjectId = input.SubjectId;
        exam.Notes = Trim(input.Notes, 2000);
        await db.SaveChangesAsync(ct);
        return ToDto(exam, Today());
    }

    public async Task DeleteExamAsync(Guid userId, Guid examId, CancellationToken ct)
    {
        var deleted = await db.Exams.Where(e => e.Id == examId && e.UserId == userId).ExecuteDeleteAsync(ct);
        if (deleted == 0) throw new NotFoundException("Exam");
    }

    private async Task<Exam> AddExamInternalAsync(Guid userId, ExamInput input, CancellationToken ct)
    {
        await ValidateExamAsync(input, ct);
        var exam = new Exam
        {
            Id = Ids.New(), UserId = userId, Title = input.Title.Trim(), Date = input.Date, SubjectId = input.SubjectId,
            Notes = Trim(input.Notes, 2000), CreatedAt = clock.GetUtcNow().UtcDateTime
        };
        db.Exams.Add(exam);
        return exam;
    }

    private async Task ValidateExamAsync(ExamInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 400)
            throw new ValidationException("invalid_exam_title", "Exam title is required.");
        if (input.SubjectId is not null && !await db.Subjects.AnyAsync(s => s.Id == input.SubjectId, ct))
            throw new ValidationException("invalid_subject", "Unknown subject.");
    }

    private async Task<UserPreferences> GetOrCreatePreferencesAsync(Guid userId, CancellationToken ct)
    {
        var prefs = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (prefs is not null) return prefs;
        prefs = new UserPreferences { UserId = userId };
        db.UserPreferences.Add(prefs);
        return prefs;
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private static ExamDto ToDto(Exam e, DateOnly today) => new(e.Id, e.Title, e.Date, e.SubjectId, e.Notes, e.Date.DayNumber - today.DayNumber);

    private static string OneOf(string value, string[] allowed, string field)
    {
        var v = value.Trim().ToLowerInvariant();
        return allowed.Contains(v) ? v : throw new ValidationException("invalid_" + field, $"Invalid value for {field}.");
    }

    private static string? Trim(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > max ? s[..max] : s;
    }

    public static PreferencesDto ToDto(UserPreferences p) => new(p.UiLanguage, p.Theme, p.ExplanationLanguage, p.BilingualTerms, p.TeachingStyle,
        p.MotivationEnabled, p.NotificationsEnabled, p.FocusMinutes, p.BreakMinutes, p.WeeklyGoalMinutes);

    private static ProfileDto ToDto(AppUser u, UserPreferences p, AcademicProfile a) => new(
        u.Id, u.Email ?? string.Empty, u.DisplayName, u.CreatedAt, ToDto(p),
        new AcademicProfileDto(a.Grade, a.Curriculum, a.AcademicYear, a.TargetScore, a.DailyStudyMinutes, a.DifficultSubjectIds, a.OnboardingCompleted));
}
