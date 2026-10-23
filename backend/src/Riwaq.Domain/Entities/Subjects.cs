namespace Riwaq.Domain.Entities;

/// <summary>A school subject. The seven core subjects are seeded; colours and icons are configurable.</summary>
public class Subject
{
    public int Id { get; set; }

    /// <summary>Stable code used by the frontend for subject-specific tools, e.g. "physics".</summary>
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Color { get; set; } = "#0F766E";
    public string Icon { get; set; } = "book";
    public int SortOrder { get; set; }

    /// <summary>Primary language of the subject's content ("ar", "en", "fr").</summary>
    public string ContentLanguage { get; set; } = "ar";
}

/// <summary>An upcoming examination configured by the student.</summary>
public class Exam
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int? SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}
