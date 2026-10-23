namespace Riwaq.Domain.Entities;

public class ReadingProgress
{
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public int LastPage { get; set; } = 1;

    /// <summary>Bitset of pages the student actually spent time on (bit n-1 = page n).</summary>
    public byte[] ReadPages { get; set; } = [];
    public int ReadPageCount { get; set; }
    public int TotalSeconds { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ChapterProgress
{
    public Guid UserId { get; set; }
    public Guid ChapterId { get; set; }
    public Guid BookId { get; set; }
    public ChapterStatus Status { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class Bookmark
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public int PageNumber { get; set; }
    public string? Label { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>A highlight on a page, optionally carrying a personal annotation.</summary>
public class Highlight
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BookId { get; set; }
    public int PageNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Color { get; set; } = "yellow";

    /// <summary>JSON array of rectangles in page-relative fractions: [{x,y,w,h}].</summary>
    public string RectsJson { get; set; } = "[]";
    public string? Annotation { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
