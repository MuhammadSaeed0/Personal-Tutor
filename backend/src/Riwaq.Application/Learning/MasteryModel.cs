namespace Riwaq.Application.Learning;

/// <summary>
/// Configurable topic-mastery estimate. Each piece of evidence moves the estimate toward 1 (success)
/// or 0 (failure) with a learning rate that shrinks as evidence accumulates, starting from a neutral prior.
/// The result is an estimate, not a probability of passing an exam, and the UI labels it as such.
/// </summary>
public static class MasteryModel
{
    public const double Prior = 0.3;

    /// <summary>Weight of a quiz answer relative to a flashcard review.</summary>
    public const double QuestionWeight = 1.0;
    public const double CardWeight = 0.5;

    public static double Update(double current, int evidenceCountBefore, bool success, double weight)
    {
        if (evidenceCountBefore == 0 && current == 0) current = Prior;
        // Early evidence moves the estimate quickly; later evidence is smoothed (min rate keeps recency).
        var rate = Math.Max(0.15, 1.0 / (evidenceCountBefore + 2)) * weight;
        var target = success ? 1.0 : 0.0;
        return Math.Clamp(current + rate * (target - current), 0, 1);
    }

    public static double Update(double current, int evidenceCountBefore, double fraction, double weight)
    {
        if (evidenceCountBefore == 0 && current == 0) current = Prior;
        var rate = Math.Max(0.15, 1.0 / (evidenceCountBefore + 2)) * weight;
        return Math.Clamp(current + rate * (fraction - current), 0, 1);
    }

    /// <summary>"weak" &lt; 0.5 ≤ "developing" &lt; 0.8 ≤ "strong". Requires at least 3 pieces of evidence.</summary>
    public static string Band(double mastery, int evidence) =>
        evidence < 3 ? "insufficient" : mastery < 0.5 ? "weak" : mastery < 0.8 ? "developing" : "strong";
}

public static class StreakCalculator
{
    /// <summary>
    /// Counts consecutive active days ending today (or yesterday, so a streak isn't lost before today's study).
    /// </summary>
    public static (int Current, int Longest) Compute(IEnumerable<DateOnly> activeDays, DateOnly today)
    {
        var days = activeDays.Distinct().OrderBy(d => d).ToList();
        if (days.Count == 0) return (0, 0);

        int longest = 1, run = 1;
        for (var i = 1; i < days.Count; i++)
        {
            run = days[i].DayNumber - days[i - 1].DayNumber == 1 ? run + 1 : 1;
            longest = Math.Max(longest, run);
        }

        var set = days.ToHashSet();
        var cursor = set.Contains(today) ? today : today.AddDays(-1);
        var current = 0;
        while (set.Contains(cursor))
        {
            current++;
            cursor = cursor.AddDays(-1);
        }
        return (current, longest);
    }
}
