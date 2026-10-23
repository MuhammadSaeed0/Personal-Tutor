using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Riwaq.Application.Text;
using Riwaq.Domain;

namespace Riwaq.Application.Learning;

public sealed record GradeResult(bool? IsCorrect, double Fraction, GradingMethod Method);

/// <summary>
/// Deterministic grading for every question type that can be graded reliably without an AI model.
/// Answer-key JSON shapes (all produced and validated by the quiz generator):
///   MultipleChoice: {"correct": 2}                              answer: 2
///   TrueFalse:      {"correct": true}                           answer: true
///   FillBlank:      {"accepted": ["photosynthesis", "..."]}     answer: "text"
///   Numerical:      {"value": 9.8, "tolerance": 0.05, "unit": "m/s²"}  answer: "9.81" (tolerance is relative)
///   Matching:       {"pairs": [0, 2, 1]}  (left i ↔ right pairs[i]) answer: [0, 2, 1]
///   Ordering:       {"order": [2, 0, 1]}                         answer: [2, 0, 1]
///   ShortAnswer / Structured: {"modelAnswer": "...", "rubric": ["point 1", ...]} → AI rubric or self-assessment.
/// </summary>
public static class AnswerGrader
{
    public static bool IsDeterministic(QuestionType type) =>
        type is not (QuestionType.ShortAnswer or QuestionType.Structured);

    public static GradeResult Grade(QuestionType type, string answerKeyJson, string answerJson)
    {
        if (!IsDeterministic(type)) return new GradeResult(null, 0, GradingMethod.Pending);

        JsonNode? key, answer;
        try
        {
            key = JsonNode.Parse(answerKeyJson);
            answer = JsonNode.Parse(answerJson);
        }
        catch (JsonException)
        {
            return new GradeResult(false, 0, GradingMethod.Deterministic);
        }
        if (key is null || answer is null) return new GradeResult(false, 0, GradingMethod.Deterministic);

        var fraction = type switch
        {
            QuestionType.MultipleChoice => TryInt(answer) is { } a && a == key["correct"]?.GetValue<int>() ? 1 : 0,
            QuestionType.TrueFalse => TryBool(answer) is { } b && b == key["correct"]?.GetValue<bool>() ? 1 : 0,
            QuestionType.FillBlank => GradeFillBlank(key, answer),
            QuestionType.Numerical => GradeNumerical(key, answer),
            QuestionType.Matching => GradeSequence(key["pairs"], answer, partialCredit: true),
            QuestionType.Ordering => GradeSequence(key["order"], answer, partialCredit: false),
            _ => 0
        };
        return new GradeResult(fraction >= 0.999, fraction, GradingMethod.Deterministic);
    }

    private static double GradeFillBlank(JsonNode key, JsonNode answer)
    {
        var given = Normalize(answer.GetValueKind() == JsonValueKind.String ? answer.GetValue<string>() : answer.ToJsonString());
        if (given.Length == 0) return 0;
        var accepted = key["accepted"]?.AsArray().Select(n => Normalize(n?.GetValue<string>())) ?? [];
        return accepted.Any(a => a.Length > 0 && a == given) ? 1 : 0;
    }

    private static double GradeNumerical(JsonNode key, JsonNode answer)
    {
        var expected = key["value"]?.GetValue<double>();
        if (expected is null) return 0;
        var raw = answer.GetValueKind() switch
        {
            JsonValueKind.Number => answer.GetValue<double>().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.String => answer.GetValue<string>(),
            _ => ""
        };
        if (!TryParseNumber(raw, out var given)) return 0;

        var tolerance = key["tolerance"]?.GetValue<double>() ?? 0.02;
        var allowed = Math.Max(Math.Abs(expected.Value) * tolerance, 1e-9);
        return Math.Abs(given - expected.Value) <= allowed ? 1 : 0;
    }

    private static double GradeSequence(JsonNode? expectedNode, JsonNode answer, bool partialCredit)
    {
        if (expectedNode is not JsonArray expected || answer is not JsonArray given || given.Count != expected.Count || expected.Count == 0)
            return 0;
        var correct = 0;
        for (var i = 0; i < expected.Count; i++)
        {
            if (TryInt(given[i]) is { } g && g == expected[i]!.GetValue<int>()) correct++;
        }
        if (!partialCredit) return correct == expected.Count ? 1 : 0;
        return (double)correct / expected.Count;
    }

    /// <summary>Accepts "9.8", "9,8", "٩٫٨", "9.8 m/s²", "1.2e3", "1.2×10^3".</summary>
    public static bool TryParseNumber(string? raw, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = ArabicText.NormalizeDigits(raw.Trim()).Replace('٫', '.').Replace('،', ',').Replace('−', '-');

        // Scientific notation written as a×10^b
        var times = s.IndexOfAny(['×', 'x', '*']);
        if (times > 0 && s.IndexOf("10^", times, StringComparison.Ordinal) > times)
        {
            var mantissa = s[..times];
            var exponent = s[(s.IndexOf("10^", StringComparison.Ordinal) + 3)..];
            if (TryParseNumber(mantissa, out var m) && TryParseNumber(new string(exponent.TakeWhile(c => char.IsDigit(c) || c == '-').ToArray()), out var e))
            {
                value = m * Math.Pow(10, e);
                return true;
            }
        }

        var numeric = new string(s.TakeWhile(c => char.IsDigit(c) || c is '.' or ',' or '-' or '+' or 'e' or 'E').ToArray());
        if (numeric.Count(c => c == ',') == 1 && !numeric.Contains('.')) numeric = numeric.Replace(',', '.');
        else numeric = numeric.Replace(",", "");
        return double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static string Normalize(string? text)
    {
        var folded = ArabicText.FoldForMatching(text);
        var chars = folded.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static int? TryInt(JsonNode? node)
    {
        if (node is null) return null;
        return node.GetValueKind() switch
        {
            JsonValueKind.Number => node.GetValue<int>(),
            JsonValueKind.String when int.TryParse(node.GetValue<string>(), out var i) => i,
            _ => null
        };
    }

    private static bool? TryBool(JsonNode node) => node.GetValueKind() switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String when bool.TryParse(node.GetValue<string>(), out var b) => b,
        _ => null
    };
}
