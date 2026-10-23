using Microsoft.AspNetCore.Identity;

namespace Riwaq.Domain.Entities;

public class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public UserPreferences? Preferences { get; set; }
    public AcademicProfile? AcademicProfile { get; set; }
}

public class UserPreferences
{
    public Guid UserId { get; set; }

    /// <summary>Interface language: "ar" (default, RTL) or "en" (LTR).</summary>
    public string UiLanguage { get; set; } = "ar";

    /// <summary>"light", "dark" or "system".</summary>
    public string Theme { get; set; } = "system";

    /// <summary>Language the tutor answers in. Independent of the textbook language.</summary>
    public string ExplanationLanguage { get; set; } = "ar";

    /// <summary>When true the tutor adds original-language terminology next to translated terms.</summary>
    public bool BilingualTerms { get; set; } = true;

    /// <summary>"guided" (intuition → concepts → example → check) or "concise".</summary>
    public string TeachingStyle { get; set; } = "guided";

    public bool MotivationEnabled { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public int WeeklyGoalMinutes { get; set; } = 600;
}

public class AcademicProfile
{
    public Guid UserId { get; set; }
    public string? Grade { get; set; }
    public string? Curriculum { get; set; }
    public string? AcademicYear { get; set; }
    public int? TargetScore { get; set; }
    public int DailyStudyMinutes { get; set; } = 120;
    public List<int> DifficultSubjectIds { get; set; } = [];
    public bool OnboardingCompleted { get; set; }
}

/// <summary>Rotating refresh token. Only a SHA-256 hash is stored.</summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByHash { get; set; }
    public string? UserAgent { get; set; }
    public DateTime LastUsedAt { get; set; }

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;
}
