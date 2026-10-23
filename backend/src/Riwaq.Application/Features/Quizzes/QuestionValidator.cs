using System.Text.Json;
using System.Text.Json.Nodes;
using Riwaq.Application.Learning;
using Riwaq.Application.Text;
using Riwaq.Domain;

namespace Riwaq.Application.Features.Quizzes;

/// <summary>A question as produced by the model (before validation).</summary>
public sealed record RawQuestion(
    string? Type, string? Prompt, List<string>? Options, List<string>? MatchRight, int? CorrectIndex, bool? CorrectBool,
    List<string>? AcceptedAnswers, double? NumericAnswer, double? Tolerance, string? Unit, string? ModelAnswer, List<string>? Rubric,
    string? Explanation, string? Hint, string? Difficulty, string? Topic, int? Page, string? SourceQuote);

public sealed record ValidatedQuestion(
    QuestionType Type, string Prompt, string OptionsJson, string AnswerKeyJson, string Explanation, string? Hint,
    Difficulty Difficulty, string? Topic, int? SourcePage, string? SourceQuote);

public sealed record ValidationOutcome(ValidatedQuestion? Question, string? RejectionReason);

/// <summary>
/// Validates and normalises AI-generated questions. Questions that are structurally invalid, ambiguous
/// (duplicate options), or not supported by the supplied source text are rejected rather than shown.
/// Choices, matches and ordering items are shuffled server-side, so answer keys never follow model order.
/// </summary>
public static class QuestionValidator
{
    public static ValidationOutcome Validate(RawQuestion raw, IReadOnlyDictionary<int, string> sourcePages, Random random)
    {
        var type = ParseType(raw.Type);
        if (type is null) return Reject("unknown_type");
        var prompt = raw.Prompt?.Trim() ?? "";
        if (prompt.Length is < 5 or > 2000) return Reject("invalid_prompt");
        var explanation = raw.Explanation?.Trim() ?? "";
        if (explanation.Length < 5) return Reject("missing_explanation");

        // Grounding: the cited quote must really occur on the cited page (or somewhere in the source).
        var (page, quote, grounded) = CheckGrounding(raw.Page, raw.SourceQuote, sourcePages);
        if (!grounded) return Reject("not_supported_by_source");

        string options, key;
        switch (type)
        {
            case QuestionType.MultipleChoice:
            {
                var choices = Clean(raw.Options);
                if (choices.Count is < 3 or > 6 || HasDuplicates(choices)) return Reject("invalid_choices");
                if (raw.CorrectIndex is not int correct || correct < 0 || correct >= choices.Count) return Reject("invalid_answer");
                var order = Shuffle(choices.Count, random);
                options = Json(new { choices = order.Select(i => choices[i]).ToArray() });
                key = Json(new { correct = Array.IndexOf(order, correct) });
                break;
            }
            case QuestionType.TrueFalse:
                if (raw.CorrectBool is not bool b) return Reject("invalid_answer");
                options = "{}";
                key = Json(new { correct = b });
                break;
            case QuestionType.FillBlank:
            {
                var accepted = Clean(raw.AcceptedAnswers).Where(a => a.Length <= 120).ToList();
                if (accepted.Count == 0) return Reject("invalid_answer");
                options = "{}";
                key = Json(new { accepted });
                break;
            }
            case QuestionType.Numerical:
                if (raw.NumericAnswer is not double value || double.IsNaN(value) || double.IsInfinity(value)) return Reject("invalid_answer");
                options = Json(new { unit = raw.Unit?.Trim() });
                key = Json(new { value, tolerance = Math.Clamp(raw.Tolerance ?? 0.02, 0.001, 0.1), unit = raw.Unit?.Trim() });
                break;
            case QuestionType.Matching:
            {
                var left = Clean(raw.Options);
                var right = Clean(raw.MatchRight);
                if (left.Count is < 3 or > 6 || right.Count != left.Count || HasDuplicates(left) || HasDuplicates(right)) return Reject("invalid_matching");
                var order = Shuffle(right.Count, random);           // displayed right column = right[order[k]]
                var pairs = Enumerable.Range(0, left.Count).Select(i => Array.IndexOf(order, i)).ToArray();
                options = Json(new { left, right = order.Select(i => right[i]).ToArray() });
                key = Json(new { pairs });
                break;
            }
            case QuestionType.Ordering:
            {
                var items = Clean(raw.Options);                     // model supplies the correct order
                if (items.Count is < 3 or > 8 || HasDuplicates(items)) return Reject("invalid_ordering");
                var order = Shuffle(items.Count, random);
                if (order.SequenceEqual(Enumerable.Range(0, items.Count))) (order[0], order[1]) = (order[1], order[0]);
                var displayed = order.Select(i => items[i]).ToArray();
                // Correct answer: displayed indices listed in the true order.
                var correctOrder = Enumerable.Range(0, items.Count).Select(i => Array.IndexOf(order, i)).ToArray();
                options = Json(new { items = displayed });
                key = Json(new { order = correctOrder });
                break;
            }
            default: // ShortAnswer, Structured
            {
                var model = raw.ModelAnswer?.Trim() ?? "";
                if (model.Length < 2) return Reject("missing_model_answer");
                options = "{}";
                key = Json(new { modelAnswer = model, rubric = Clean(raw.Rubric).Take(8).ToList() });
                break;
            }
        }

        var difficulty = raw.Difficulty?.ToLowerInvariant() switch { "easy" => Difficulty.Easy, "hard" => Difficulty.Hard, _ => Difficulty.Medium };
        var topic = string.IsNullOrWhiteSpace(raw.Topic) ? null : raw.Topic.Trim().Length > 200 ? raw.Topic.Trim()[..200] : raw.Topic.Trim();
        var hint = string.IsNullOrWhiteSpace(raw.Hint) ? null : raw.Hint.Trim().Length > 1000 ? raw.Hint.Trim()[..1000] : raw.Hint.Trim();
        return new ValidationOutcome(new ValidatedQuestion(type.Value, prompt, options, key, explanation, hint, difficulty, topic, page, quote), null);
    }

    public static QuestionType? ParseType(string? type) => type?.Trim().ToLowerInvariant().Replace("-", "_") switch
    {
        "multiple_choice" or "mcq" => QuestionType.MultipleChoice,
        "true_false" => QuestionType.TrueFalse,
        "fill_blank" or "fill_in_the_blank" => QuestionType.FillBlank,
        "short_answer" => QuestionType.ShortAnswer,
        "matching" => QuestionType.Matching,
        "ordering" => QuestionType.Ordering,
        "numerical" => QuestionType.Numerical,
        "structured" => QuestionType.Structured,
        _ => null
    };

    public static string TypeName(QuestionType type) => type switch
    {
        QuestionType.MultipleChoice => "multiple_choice",
        QuestionType.TrueFalse => "true_false",
        QuestionType.FillBlank => "fill_blank",
        QuestionType.ShortAnswer => "short_answer",
        QuestionType.Matching => "matching",
        QuestionType.Ordering => "ordering",
        QuestionType.Numerical => "numerical",
        _ => "structured"
    };

    /// <summary>
    /// A quote counts as found when at least 60% of its tokens appear in the page text (tolerates OCR noise
    /// and small paraphrases). Without a quote the cited page must exist in the source.
    /// </summary>
    internal static (int? Page, string? Quote, bool Grounded) CheckGrounding(int? page, string? quote, IReadOnlyDictionary<int, string> pages)
    {
        if (pages.Count == 0) return (null, null, true); // e.g. generated from a selection without page context
        var cleanQuote = string.IsNullOrWhiteSpace(quote) ? null : quote.Trim().Length > 1000 ? quote.Trim()[..1000] : quote.Trim();

        if (cleanQuote is null)
            return page is int p && pages.ContainsKey(p) ? (p, null, true) : (null, null, false);

        if (page is int cited && pages.TryGetValue(cited, out var citedText) && QuoteMatches(cleanQuote, citedText))
            return (cited, cleanQuote, true);

        foreach (var (number, text) in pages)
            if (QuoteMatches(cleanQuote, text)) return (number, cleanQuote, true);
        return (null, null, false);
    }

    private static bool QuoteMatches(string quote, string text)
    {
        var quoteTokens = SearchTokenizer.Tokenize(quote).Distinct().ToList();
        if (quoteTokens.Count == 0) return false;
        var textTokens = SearchTokenizer.Tokenize(text).ToHashSet();
        return quoteTokens.Count(textTokens.Contains) >= Math.Ceiling(quoteTokens.Count * 0.6);
    }

    private static List<string> Clean(List<string>? items) =>
        items?.Select(i => i?.Trim() ?? "").Where(i => i.Length is > 0 and <= 500).ToList() ?? [];

    private static bool HasDuplicates(List<string> items) =>
        items.Select(AnswerGrader.Normalize).Distinct().Count() != items.Count;

    private static int[] Shuffle(int count, Random random)
    {
        var order = Enumerable.Range(0, count).ToArray();
        random.Shuffle(order);
        return order;
    }

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static ValidationOutcome Reject(string reason) => new(null, reason);
}

/// <summary>Human-readable correct answers for review screens and flashcards.</summary>
public static class QuizAnswerText
{
    public static string Describe(Domain.Entities.QuizQuestion q, string language = "ar")
    {
        try
        {
            var key = JsonNode.Parse(q.AnswerKeyJson)!;
            var options = JsonNode.Parse(q.OptionsJson);
            return q.Type switch
            {
                QuestionType.MultipleChoice => options!["choices"]![key["correct"]!.GetValue<int>()]!.GetValue<string>(),
                QuestionType.TrueFalse => key["correct"]!.GetValue<bool>()
                    ? language == "ar" ? "صح" : language == "fr" ? "Vrai" : "True"
                    : language == "ar" ? "خطأ" : language == "fr" ? "Faux" : "False",
                QuestionType.FillBlank => string.Join(" / ", key["accepted"]!.AsArray().Select(a => a!.GetValue<string>())),
                QuestionType.Numerical => $"{key["value"]!.GetValue<double>()} {key["unit"]?.GetValue<string>()}".Trim(),
                QuestionType.Matching => string.Join("\n", options!["left"]!.AsArray().Select((l, i) =>
                    $"{l!.GetValue<string>()} → {options["right"]![key["pairs"]![i]!.GetValue<int>()]!.GetValue<string>()}")),
                QuestionType.Ordering => string.Join(" → ", key["order"]!.AsArray().Select(i => options!["items"]![i!.GetValue<int>()]!.GetValue<string>())),
                _ => key["modelAnswer"]?.GetValue<string>() ?? ""
            };
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
