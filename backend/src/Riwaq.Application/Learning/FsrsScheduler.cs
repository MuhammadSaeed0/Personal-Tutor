using Riwaq.Domain;

namespace Riwaq.Application.Learning;

public sealed record FsrsCard(CardState State, DateTime Due, double Stability, double Difficulty, int Step, int Reps, int Lapses, DateTime? LastReview);

public sealed record FsrsReviewResult(FsrsCard Card, double ElapsedDays, double ScheduledDays, double? Retrievability);

/// <summary>
/// FSRS-5 spaced-repetition scheduler (Free Spaced Repetition Scheduler, open-spaced-repetition project)
/// with the published default parameters. Implements learning/relearning steps, stability and difficulty
/// updates, same-day review handling and interval fuzz-free scheduling for a target retention.
/// </summary>
public sealed class FsrsScheduler
{
    public static readonly double[] DefaultWeights =
    [
        0.40255, 1.18385, 3.173, 15.69105, 7.1949, 0.5345, 1.4604, 0.0046, 1.54575, 0.1192,
        1.01925, 1.9395, 0.11, 0.29605, 2.2698, 0.2315, 2.9898, 0.51655, 0.6621
    ];

    private const double Decay = -0.5;
    private static readonly double Factor = Math.Pow(0.9, 1 / Decay) - 1; // = 19/81

    private readonly double[] _w;
    private readonly double _desiredRetention;
    private readonly int _maximumInterval;
    private readonly TimeSpan[] _learningSteps = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10)];
    private readonly TimeSpan[] _relearningSteps = [TimeSpan.FromMinutes(10)];

    public FsrsScheduler(double desiredRetention = 0.9, int maximumInterval = 36500, double[]? weights = null)
    {
        _w = weights ?? DefaultWeights;
        _desiredRetention = desiredRetention;
        _maximumInterval = maximumInterval;
    }

    public static FsrsCard NewCard(DateTime now) => new(CardState.New, now, 0, 0, 0, 0, 0, null);

    public double Retrievability(FsrsCard card, DateTime now)
    {
        if (card.LastReview is null || card.Stability <= 0) return 0;
        var elapsed = Math.Max(0, (now - card.LastReview.Value).TotalDays);
        return Math.Pow(1 + Factor * elapsed / card.Stability, Decay);
    }

    public FsrsReviewResult Review(FsrsCard card, ReviewRating rating, DateTime now)
    {
        var elapsedDays = card.LastReview is null ? 0 : Math.Max(0, (now - card.LastReview.Value).TotalDays);
        double? r = card.State == CardState.New ? null : Retrievability(card, now);
        var g = (int)rating;

        double s, d;
        if (card.State == CardState.New)
        {
            s = InitialStability(g);
            d = InitialDifficulty(g);
        }
        else if (elapsedDays < 1)
        {
            // Same-day review: short-term stability update.
            s = ShortTermStability(card.Stability, g);
            d = NextDifficulty(card.Difficulty, g);
        }
        else
        {
            d = NextDifficulty(card.Difficulty, g);
            s = rating == ReviewRating.Again
                ? ForgetStability(card.Difficulty, card.Stability, r!.Value)
                : RecallStability(card.Difficulty, card.Stability, r!.Value, rating);
        }

        var state = card.State;
        var step = card.Step;
        var lapses = card.Lapses;
        TimeSpan interval;

        switch (card.State)
        {
            case CardState.New:
            case CardState.Learning:
                (state, step, interval) = LearningTransition(card.State == CardState.New ? 0 : step, rating, s, _learningSteps);
                break;

            case CardState.Relearning:
                (state, step, interval) = LearningTransition(step, rating, s, _relearningSteps);
                if (state == CardState.Learning) state = CardState.Relearning;
                break;

            default: // Review
                if (rating == ReviewRating.Again)
                {
                    lapses++;
                    state = CardState.Relearning;
                    step = 0;
                    interval = _relearningSteps[0];
                }
                else
                {
                    interval = TimeSpan.FromDays(ReviewInterval(card, s, rating, now));
                }
                break;
        }

        var next = new FsrsCard(state, now + interval, s, d, step, card.Reps + 1, lapses, now);
        return new FsrsReviewResult(next, elapsedDays, interval.TotalDays, r);
    }

    private (CardState State, int Step, TimeSpan Interval) LearningTransition(int step, ReviewRating rating, double s, TimeSpan[] steps)
    {
        switch (rating)
        {
            case ReviewRating.Again:
                return (CardState.Learning, 0, steps[0]);
            case ReviewRating.Hard:
                // Hard repeats the current step (a bit longer on the first step).
                var hard = step == 0 && steps.Length > 1
                    ? TimeSpan.FromTicks((steps[0].Ticks + steps[1].Ticks) / 2)
                    : steps[Math.Min(step, steps.Length - 1)];
                return (CardState.Learning, step, hard);
            case ReviewRating.Good:
                if (step + 1 < steps.Length) return (CardState.Learning, step + 1, steps[step + 1]);
                return (CardState.Review, 0, TimeSpan.FromDays(NextInterval(s)));
            default: // Easy graduates immediately
                return (CardState.Review, 0, TimeSpan.FromDays(Math.Max(NextInterval(s), 1)));
        }
    }

    private double ReviewInterval(FsrsCard card, double newStability, ReviewRating rating, DateTime now)
    {
        var interval = NextInterval(newStability);
        // Keep Hard ≤ Good ≤ Easy ordering relative to the previous interval.
        var lastInterval = card.LastReview is null ? 0 : Math.Max(1, Math.Round((card.Due - card.LastReview.Value).TotalDays));
        return rating switch
        {
            ReviewRating.Hard => Math.Max(1, Math.Min(interval, Math.Max(lastInterval, 1))),
            ReviewRating.Easy => Math.Max(interval, lastInterval + 1),
            _ => interval
        };
    }

    public double NextInterval(double stability)
    {
        var interval = stability / Factor * (Math.Pow(_desiredRetention, 1 / Decay) - 1);
        return Math.Clamp(Math.Round(interval), 1, _maximumInterval);
    }

    private double InitialStability(int g) => Math.Max(_w[g - 1], 0.1);

    private double InitialDifficulty(int g) => Math.Clamp(_w[4] - Math.Exp(_w[5] * (g - 1)) + 1, 1, 10);

    private double NextDifficulty(double d, int g)
    {
        var delta = -_w[6] * (g - 3);
        var damped = d + delta * (10 - d) / 9;
        var reverted = _w[7] * InitialDifficulty(4) + (1 - _w[7]) * damped;
        return Math.Clamp(reverted, 1, 10);
    }

    private double ShortTermStability(double s, int g) => Math.Max(0.1, s * Math.Exp(_w[17] * (g - 3 + _w[18])));

    private double RecallStability(double d, double s, double r, ReviewRating rating)
    {
        var hardPenalty = rating == ReviewRating.Hard ? _w[15] : 1;
        var easyBonus = rating == ReviewRating.Easy ? _w[16] : 1;
        return s * (1 + Math.Exp(_w[8]) * (11 - d) * Math.Pow(s, -_w[9]) * (Math.Exp((1 - r) * _w[10]) - 1) * hardPenalty * easyBonus);
    }

    private double ForgetStability(double d, double s, double r)
    {
        var longTerm = _w[11] * Math.Pow(d, -_w[12]) * (Math.Pow(s + 1, _w[13]) - 1) * Math.Exp((1 - r) * _w[14]);
        var shortTermCap = s / Math.Exp(_w[17] * _w[18]);
        return Math.Max(0.1, Math.Min(longTerm, shortTermCap));
    }

    /// <summary>A card counts as mastered only after real reviews have pushed its stability past the threshold.</summary>
    public static bool IsMastered(CardState state, double stability, double thresholdDays = 21) =>
        state == CardState.Review && stability >= thresholdDays;
}
