using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Riwaq.Application.Features.Quizzes;
using Riwaq.Domain;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Features.Tutor;

/// <summary>
/// Last line of defence for the exam assistant: the prompt already forbids answers, and this check withholds a reply
/// that still contains one: the correct choice's text, an accepted answer, the numeric result or a true/false verdict.
/// The question on screen is checked strictly; the other questions of the exam only for long, unmistakable matches.
/// </summary>
public static partial class ExamAnswerGuard
{
    public static bool Reveals(string reply, QuizQuestion? current, IEnumerable<QuizQuestion> all)
    {
        var text = QuizDiagram.Normalize(reply);
        var numbers = Numbers(reply);
        if (current is not null && Reveals(reply, text, numbers, current, strict: true)) return true;
        return all.Where(q => q.Id != current?.Id).Any(q => Reveals(reply, text, numbers, q, strict: false));
    }

    private static bool Reveals(string reply, string text, List<double> numbers, QuizQuestion q, bool strict)
    {
        JsonNode? key, options;
        try
        {
            key = JsonNode.Parse(q.AnswerKeyJson);
            options = JsonNode.Parse(q.OptionsJson);
        }
        catch (Exception)
        {
            return false;
        }
        if (key is null) return false;
        var minLetters = strict ? 4 : 8;

        bool Contains(string? answer)
        {
            if (string.IsNullOrWhiteSpace(answer)) return false;
            var a = QuizDiagram.Normalize(answer);
            // Answers that are nothing but a number are handled by the numeric check.
            return a.Count(char.IsLetter) >= minLetters && text.Contains(a, StringComparison.Ordinal);
        }

        switch (q.Type)
        {
            case QuestionType.MultipleChoice:
                var index = key["correct"]?.GetValue<int>();
                var choices = options?["choices"]?.AsArray();
                if (index is int i && choices is not null && i >= 0 && i < choices.Count && Contains(choices[i]?.GetValue<string>())) return true;
                break;
            case QuestionType.TrueFalse:
                if (strict && TrueFalseVerdict().IsMatch(reply)) return true;
                break;
            case QuestionType.Numerical:
                var value = key["value"]?.GetValue<double>();
                if (value is double v)
                {
                    var tolerance = Math.Max(key["tolerance"]?.GetValue<double>() ?? 0, Math.Abs(v) * 0.01);
                    var given = Numbers(q.Prompt);
                    // A value the question itself states is not a giveaway.
                    if (!given.Any(g => Math.Abs(g - v) <= tolerance) && numbers.Any(n => Math.Abs(n - v) <= tolerance)) return true;
                }
                break;
        }

        if (key["accepted"] is JsonArray accepted && accepted.Any(a => Contains(a?.GetValue<string>()))) return true;
        return false;
    }

    private static List<double> Numbers(string value)
    {
        var digits = new string(value.Select(c => c switch
        {
            >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
            >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
            '٫' => '.',
            _ => c
        }).ToArray());
        return NumberPattern().Matches(digits)
            .Select(m => double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN)
            .Where(d => !double.IsNaN(d)).ToList();
    }

    /// <summary>What the student sees instead of a reply that gave the answer away.</summary>
    public static string SafeReply(string language, string? topic) => language switch
    {
        "en" => "I can't give or confirm the answer to an exam question while the exam is running. "
                + (topic is null ? "Ask me about the meaning of a term or the idea behind it instead." : $"Ask me about the general idea of \"{topic}\" instead, without this question's values."),
        "fr" => "Je ne peux pas donner ni confirmer la réponse d'une question pendant l'examen. "
                + (topic is null ? "Demande-moi plutôt le sens d'un terme ou l'idée générale." : $"Demande-moi plutôt l'idée générale de « {topic} », sans les valeurs de la question."),
        _ => "لا أستطيع أن أعطيك إجابة سؤال الامتحان أو أؤكدها أثناء الامتحان. "
             + (topic is null ? "اسألني بدلًا من ذلك عن معنى مصطلح أو عن الفكرة العامة." : $"اسألني بدلًا من ذلك عن الفكرة العامة لـ«{topic}» دون قيم هذا السؤال.")
    };

    [GeneratedRegex(@"-?\d+(?:[.,]\d+)?")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"(العبارة|الجملة|الإجابة|العبارة السابقة)\s+(صحيحة|خاطئة|خطأ|صح|صحيح)|\b(statement|answer|it)\s+is\s+(true|false|correct|incorrect)\b|\b(l'affirmation|la réponse)\s+est\s+(vraie|fausse)",
        RegexOptions.IgnoreCase)]
    private static partial Regex TrueFalseVerdict();
}
