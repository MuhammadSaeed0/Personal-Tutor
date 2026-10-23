using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Riwaq.Application.Abstractions;
using Riwaq.Application.Common;
using Riwaq.Application.Features.Ai;
using Riwaq.Application.Text;
using Riwaq.Domain;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.Summaries;

public sealed record GenerateSummaryRequest(
    Guid BookId,
    SummaryScope Scope,
    SummaryStyle Style,
    string? Language = null,
    int? Page = null,
    int? PageStart = null,
    int? PageEnd = null,
    Guid? ChapterId = null,
    string? SelectedText = null);

public sealed record SummaryDto(
    Guid Id, Guid BookId, Guid? ChapterId, SummaryScope Scope, SummaryStyle Style, string Language, int? PageStart, int? PageEnd,
    string Title, string Content, IReadOnlyList<SourceReference> Citations, IReadOnlyList<string> Warnings, bool IsEdited,
    DateTime CreatedAt, DateTime UpdatedAt);

public sealed record UpdateSummaryRequest(string? Title, string? Content);

/// <summary>Source text gathered for a generation request (shared by summaries, flashcards and quizzes).</summary>
public sealed record SourceMaterial(Book Book, Subject Subject, Chapter? Chapter, List<(int Page, string Text)> Pages, int? PageStart, int? PageEnd, List<string> Warnings, string? SelectedText);

public sealed partial class SummaryService(
    IAppDbContext db,
    IChatModel chat,
    AiUsageGuard usage,
    IConfiguration configuration,
    TimeProvider clock)
{
    private int SingleCallChars => configuration.GetValue("Summaries:SingleCallChars", 180_000);
    private int BatchChars => configuration.GetValue("Summaries:BatchChars", 120_000);
    private int MaxBatches => configuration.GetValue("Summaries:MaxBatches", 12);

    public async Task<SummaryDto> GenerateAsync(Guid userId, GenerateSummaryRequest request, CancellationToken ct)
    {
        await usage.EnsureAllowedAsync(userId, ct);
        var source = await LoadSourceAsync(db, userId, request.BookId, request.Scope, request.Page, request.PageStart, request.PageEnd, request.ChapterId, request.SelectedText, ct);
        var prefs = await db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct) ?? new UserPreferences();
        var language = request.Language is "ar" or "en" or "fr" ? request.Language : prefs.ExplanationLanguage;
        var warnings = source.Warnings;

        string raw;
        var allowedPages = source.Pages.Select(p => p.Page).ToHashSet();
        var totalChars = source.Pages.Sum(p => p.Text.Length) + (source.SelectedText?.Length ?? 0);

        if (totalChars <= SingleCallChars)
        {
            raw = await CallAsync(userId, "summary", SystemPrompt(request.Style, language, source.Subject.Code), UserTurn(source), 12000, ct);
        }
        else
        {
            // Map: condense each batch of pages into cited notes. Reduce: write the final resource from the notes.
            var batches = Batch(source.Pages, BatchChars).ToList();
            if (batches.Count > MaxBatches)
            {
                var lastIncluded = batches[MaxBatches - 1].Last().Page;
                warnings.Add($"source_truncated|{lastIncluded}");
                batches = batches.Take(MaxBatches).ToList();
                allowedPages = batches.SelectMany(b => b).Select(p => p.Page).ToHashSet();
            }

            var notes = new StringBuilder();
            foreach (var batch in batches)
            {
                var part = await CallAsync(userId, "summary", MapPrompt(language), Prompts.SourcePages(batch), 8000, ct);
                notes.AppendLine(part).AppendLine();
            }
            raw = await CallAsync(userId, "summary", SystemPrompt(request.Style, language, source.Subject.Code),
                $"<student_notes>\n{Prompts.Escape(notes.ToString())}\n</student_notes>\nThe notes above were condensed from the textbook and already carry [p:N] page citations. Write the final resource from them, keeping the citations.", 12000, ct);
        }

        var (content, citations) = ProcessPageCitations(raw, allowedPages, source.Book);
        var now = clock.GetUtcNow().UtcDateTime;
        var summary = new GeneratedSummary
        {
            Id = Ids.New(), UserId = userId, BookId = source.Book.Id, ChapterId = source.Chapter?.Id, Scope = request.Scope,
            Style = request.Style, Language = language, PageStart = source.PageStart, PageEnd = source.PageEnd,
            Title = BuildTitle(request.Style, language, source), Content = content, Citations = citations, Warnings = warnings,
            Model = chat.ModelName, CreatedAt = now, UpdatedAt = now
        };
        db.Summaries.Add(summary);
        await db.SaveChangesAsync(ct);
        return ToDto(summary);
    }

    public async Task<IReadOnlyList<SummaryDto>> ListAsync(Guid userId, Guid? bookId, CancellationToken ct)
    {
        var query = db.Summaries.AsNoTracking().Where(s => s.UserId == userId);
        if (bookId is not null) query = query.Where(s => s.BookId == bookId);
        var items = await query.OrderByDescending(s => s.UpdatedAt).Take(200).ToListAsync(ct);
        return items.Select(ToDto).ToList();
    }

    public async Task<SummaryDto> GetAsync(Guid userId, Guid id, CancellationToken ct) => ToDto(await GetOwnedAsync(userId, id, ct));

    public async Task<SummaryDto> UpdateAsync(Guid userId, Guid id, UpdateSummaryRequest request, CancellationToken ct)
    {
        var summary = await GetOwnedAsync(userId, id, ct);
        if (request.Title is not null)
        {
            var title = request.Title.Trim();
            if (title.Length is 0 or > 300) throw new ValidationException("invalid_title", "Title must be 1–300 characters.");
            summary.Title = title;
        }
        if (request.Content is not null)
        {
            if (request.Content.Length > 200_000) throw new ValidationException("content_too_long", "The summary is too long.");
            summary.Content = request.Content;
            summary.IsEdited = true;
        }
        summary.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return ToDto(summary);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var deleted = await db.Summaries.Where(s => s.Id == id && s.UserId == userId).ExecuteDeleteAsync(ct);
        if (deleted == 0) throw new NotFoundException("Summary");
    }

    /// <summary>Markdown export with a source list and an honest disclaimer.</summary>
    public async Task<(string FileName, string Markdown)> ExportAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var s = await GetOwnedAsync(userId, id, ct);
        var book = await db.Books.AsNoTracking().Where(b => b.Id == s.BookId).Select(b => b.Title).FirstOrDefaultAsync(ct);
        var md = new StringBuilder();
        md.AppendLine($"# {s.Title}").AppendLine();
        md.AppendLine($"> {book} — " + (s.Language == "ar" ? "مُولَّد بالذكاء الاصطناعي من محتوى الكتاب؛ راجعه مع كتابك ومعلّمك." : "AI-generated from the textbook content; check it against your book and teacher."));
        md.AppendLine().AppendLine(Regex.Replace(s.Content, @"\[p:(\d+)(?:-(\d+))?\]", m => m.Groups[2].Success ? $"(p. {m.Groups[1].Value}–{m.Groups[2].Value})" : $"(p. {m.Groups[1].Value})"));
        var safeName = string.Concat(s.Title.Split(Path.GetInvalidFileNameChars())).Trim();
        return ($"{(safeName.Length > 80 ? safeName[..80] : safeName)}.md", md.ToString());
    }

    // ---- Shared helpers (also used by flashcard and quiz generation) ----------------------------------

    public static async Task<SourceMaterial> LoadSourceAsync(IAppDbContext db, Guid userId, Guid bookId, SummaryScope scope,
        int? page, int? pageStart, int? pageEnd, Guid? chapterId, string? selectedText, CancellationToken ct)
    {
        var book = await db.Books.AsNoTracking().Include(b => b.Subject).FirstOrDefaultAsync(b => b.Id == bookId && b.UserId == userId, ct)
                   ?? throw new NotFoundException("Book");
        if (book.Status is not (BookStatus.Ready or BookStatus.ReadyWithWarnings))
            throw new ConflictException("book_not_ready", "This book has not finished processing yet.");

        Chapter? chapter = null;
        int from, to;
        switch (scope)
        {
            case SummaryScope.Selection:
                if (string.IsNullOrWhiteSpace(selectedText)) throw new ValidationException("selection_required", "Select some text first.");
                if (selectedText.Length > 20_000) throw new ValidationException("selection_too_long", "The selected text is too long.");
                from = to = page ?? 0;
                break;
            case SummaryScope.Page:
                from = to = page ?? throw new ValidationException("page_required", "A page number is required.");
                break;
            case SummaryScope.PageRange:
                from = pageStart ?? throw new ValidationException("page_required", "A page range is required.");
                to = pageEnd ?? from;
                if (to < from || to - from > 200) throw new ValidationException("invalid_page_range", "Choose a range of up to 200 pages.");
                break;
            case SummaryScope.Chapter:
                chapter = await db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chapterId && c.BookId == bookId, ct)
                          ?? throw new NotFoundException("Chapter");
                from = chapter.StartPage;
                to = chapter.EndPage;
                break;
            default:
                from = 1;
                to = book.PageCount;
                break;
        }

        var warnings = new List<string>();
        var pages = new List<(int, string)>();
        if (from > 0)
        {
            if (from > book.PageCount) throw new ValidationException("invalid_page_range", "The page is outside this book.");
            var rows = await db.DocumentPages.AsNoTracking()
                .Where(p => p.BookId == bookId && p.PageNumber >= from && p.PageNumber <= to)
                .OrderBy(p => p.PageNumber)
                .Select(p => new { p.PageNumber, p.Text, p.ExtractionMethod })
                .ToListAsync(ct);
            var empty = rows.Where(r => string.IsNullOrWhiteSpace(r.Text)).Select(r => r.PageNumber).ToList();
            if (empty.Count > 0) warnings.Add($"pages_without_text|{FormatPages(empty)}");
            var ocr = rows.Where(r => r.ExtractionMethod == ExtractionMethod.Ocr).Select(r => r.PageNumber).ToList();
            if (ocr.Count > 0) warnings.Add($"ocr_pages_included|{FormatPages(ocr)}");
            if (scope != SummaryScope.Selection)
                pages = rows.Where(r => !string.IsNullOrWhiteSpace(r.Text)).Select(r => (r.PageNumber, r.Text)).ToList();
            else if (rows.Count > 0)
                pages = [(rows[0].PageNumber, string.Empty)];
        }

        if (scope != SummaryScope.Selection && pages.Count == 0)
            throw new ValidationException("no_text_in_scope", "No readable text was found in the selected pages.");

        return new SourceMaterial(book, book.Subject!, chapter, pages, from > 0 ? from : null, to > 0 ? to : null, warnings, selectedText?.Trim());
    }

    /// <summary>Validates [p:N] / [p:N-M] markers against the pages actually supplied; drops the rest.</summary>
    public static (string Content, List<SourceReference> Citations) ProcessPageCitations(string raw, IReadOnlySet<int> allowedPages, Book book)
    {
        var refs = new SortedDictionary<(int, int), SourceReference>();
        var content = PageMarker().Replace(raw, m =>
        {
            var start = int.Parse(ArabicText.NormalizeDigits(m.Groups[1].Value));
            var end = m.Groups[2].Success ? int.Parse(ArabicText.NormalizeDigits(m.Groups[2].Value)) : start;
            if (end < start || !allowedPages.Contains(start) || !allowedPages.Contains(end)) return string.Empty;
            refs.TryAdd((start, end), new SourceReference { BookId = book.Id, BookTitle = book.Title, PageStart = start, PageEnd = end });
            return start == end ? $"[p:{start}]" : $"[p:{start}-{end}]";
        });
        var list = refs.Values.ToList();
        for (var i = 0; i < list.Count; i++) list[i].Marker = i + 1;
        return (content.Trim(), list);
    }

    private async Task<string> CallAsync(Guid userId, string feature, string system, string user, int maxTokens, CancellationToken ct)
    {
        ChatResult? result = null;
        try
        {
            result = await chat.CompleteAsync(new ChatRequest
            {
                Feature = feature, SystemPrompt = system, Messages = [new ChatTurn(ChatRole.User, user)], MaxTokens = maxTokens, Effort = "medium"
            }, ct);
            return result.Text;
        }
        finally
        {
            usage.Record(userId, feature, result, chat.ModelName, result is not null);
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static string UserTurn(SourceMaterial source)
    {
        if (source.SelectedText is not null)
        {
            var page = source.PageStart is null ? "" : $" page=\"{source.PageStart}\"";
            return $"<selected_text{page}>\n{Prompts.Escape(source.SelectedText)}\n</selected_text>\n" +
                   (source.PageStart is null ? "Do not add page citations." : $"Cite this text as [p:{source.PageStart}].");
        }
        return Prompts.SourcePages(source.Pages);
    }

    private static string StyleInstruction(SummaryStyle style) => style switch
    {
        SummaryStyle.Quick => "A quick summary: 5–8 short bullet points with the essentials.",
        SummaryStyle.Simple => "A simple explanation, like a friendly teacher: short paragraphs, everyday words and examples. Explain any necessary terms.",
        SummaryStyle.Detailed => "A detailed, well-structured summary with a heading for each main idea, covering all important points, definitions and examples.",
        SummaryStyle.KeyPoints => "The most important points as a prioritised bullet list grouped under short headings.",
        SummaryStyle.Definitions => "A glossary: each term in bold followed by its definition as given in the source. Keep the source's terminology (add a translation in parentheses if the source language differs from the answer language).",
        SummaryStyle.Formulas => "Every law, formula and equation in the source: the formula in LaTeX, what each symbol means and its unit, and when to use it. If the source contains no formulas, say so plainly.",
        SummaryStyle.Comparisons => "Markdown comparison tables between related concepts found in the source, followed by brief notes. Compare only things the source supports.",
        SummaryStyle.CausesEffects => "Causes and effects (and processes) found in the source as a structured list: cause → effect, each with a brief explanation.",
        SummaryStyle.ExamQuestions => "Exam-style practice questions on this content (a mix of types), each followed by a model answer. Present them as practice questions, not as real past exam questions.",
        SummaryStyle.RevisionSheet => "A one-page revision sheet: key ideas, essential definitions, formulas (if any), common mistakes, and a 5-question self-test with answers at the end.",
        _ => "A clear summary."
    };

    private static string SystemPrompt(SummaryStyle style, string language, string subjectCode) => $"""
        You create study resources for a high-school student from their textbook. Subject: {subjectCode}.
        Write in {Prompts.LanguageName(language)}. Use Markdown; math in LaTeX ($...$ / $$...$$); chemistry with \ce{"{...}"}.

        Resource to produce: {StyleInstruction(style)}

        Rules:
        - Base the content only on the supplied source. Do not add facts that are not in it.
        - After each statement, bullet or table row, cite the source page(s) as [p:N] or [p:N-M], using only page numbers that appear in the source. Never invent page numbers.
        - Preserve important terminology exactly as written in the source.
        - If the source text looks incomplete or garbled (e.g. from scanning), say so briefly instead of guessing.
        - Start directly with the content (no preamble such as "Here is your summary").

        {Prompts.SecurityRules}
        """;

    private static string MapPrompt(string language) => $"""
        You condense textbook pages into dense study notes for a later summary. Write in {Prompts.LanguageName(language)}.
        Capture every important idea, definition, formula, example, comparison and cause-effect relation as concise bullets.
        After each bullet cite its page(s) as [p:N] or [p:N-M] using only page numbers present in <source_pages>.

        {Prompts.SecurityRules}
        """;

    private static IEnumerable<List<(int Page, string Text)>> Batch(List<(int Page, string Text)> pages, int maxChars)
    {
        var batch = new List<(int, string)>();
        var size = 0;
        foreach (var p in pages)
        {
            if (batch.Count > 0 && size + p.Text.Length > maxChars)
            {
                yield return batch;
                batch = [];
                size = 0;
            }
            batch.Add(p);
            size += p.Text.Length;
        }
        if (batch.Count > 0) yield return batch;
    }

    private static string BuildTitle(SummaryStyle style, string language, SourceMaterial source)
    {
        var styleName = language switch
        {
            "en" => style switch
            {
                SummaryStyle.Quick => "Quick summary", SummaryStyle.Simple => "Simple explanation", SummaryStyle.Detailed => "Detailed summary",
                SummaryStyle.KeyPoints => "Key points", SummaryStyle.Definitions => "Definitions & terms", SummaryStyle.Formulas => "Formulas & equations",
                SummaryStyle.Comparisons => "Comparisons", SummaryStyle.CausesEffects => "Causes & effects", SummaryStyle.ExamQuestions => "Exam-style questions",
                _ => "Revision sheet"
            },
            "fr" => style switch
            {
                SummaryStyle.Quick => "Résumé rapide", SummaryStyle.Simple => "Explication simple", SummaryStyle.Detailed => "Résumé détaillé",
                SummaryStyle.KeyPoints => "Points clés", SummaryStyle.Definitions => "Définitions", SummaryStyle.Formulas => "Formules",
                SummaryStyle.Comparisons => "Comparaisons", SummaryStyle.CausesEffects => "Causes et effets", SummaryStyle.ExamQuestions => "Questions d'examen",
                _ => "Fiche de révision"
            },
            _ => style switch
            {
                SummaryStyle.Quick => "ملخص سريع", SummaryStyle.Simple => "شرح مبسط", SummaryStyle.Detailed => "ملخص تفصيلي",
                SummaryStyle.KeyPoints => "أهم النقاط", SummaryStyle.Definitions => "التعريفات والمصطلحات", SummaryStyle.Formulas => "القوانين والمعادلات",
                SummaryStyle.Comparisons => "المقارنات", SummaryStyle.CausesEffects => "الأسباب والنتائج", SummaryStyle.ExamQuestions => "أسئلة امتحانات",
                _ => "ورقة مراجعة"
            }
        };
        var pageWord = language == "ar" ? "ص" : "p.";
        var scope = source.Chapter is not null ? source.Chapter.Title
            : source.SelectedText is not null ? (language == "ar" ? "نص محدد" : language == "fr" ? "Sélection" : "Selection")
            : source.PageStart == source.PageEnd ? $"{pageWord} {source.PageStart}"
            : source.PageStart == 1 && source.PageEnd == source.Book.PageCount ? source.Book.Title
            : $"{pageWord} {source.PageStart}–{source.PageEnd}";
        var title = $"{styleName} — {scope}";
        return title.Length > 300 ? title[..300] : title;
    }

    private static string FormatPages(List<int> pages) =>
        pages.Count <= 12 ? string.Join(", ", pages) : string.Join(", ", pages.Take(12)) + $" (+{pages.Count - 12})";

    private async Task<GeneratedSummary> GetOwnedAsync(Guid userId, Guid id, CancellationToken ct) =>
        await db.Summaries.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct) ?? throw new NotFoundException("Summary");

    public static SummaryDto ToDto(GeneratedSummary s) => new(s.Id, s.BookId, s.ChapterId, s.Scope, s.Style, s.Language, s.PageStart, s.PageEnd,
        s.Title, s.Content, s.Citations, s.Warnings, s.IsEdited, s.CreatedAt, s.UpdatedAt);

    [GeneratedRegex(@"\[\s*[pP]\s*[:.]?\s*([0-9٠-٩]{1,5})\s*(?:[-–]\s*([0-9٠-٩]{1,5}))?\s*\]")]
    private static partial Regex PageMarker();
}
