using Riwaq.Application.Learning;
using Riwaq.Domain;

namespace Riwaq.Tests.Unit;

public class FsrsSchedulerTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    private readonly FsrsScheduler _fsrs = new();

    [Fact]
    public void New_card_rated_good_enters_learning_step()
    {
        var result = _fsrs.Review(FsrsScheduler.NewCard(Start), ReviewRating.Good, Start);
        Assert.Equal(CardState.Learning, result.Card.State);
        Assert.Equal(Start.AddMinutes(10), result.Card.Due);
        Assert.Equal(1, result.Card.Reps);
    }

    [Fact]
    public void New_card_rated_easy_graduates_to_review_with_day_interval()
    {
        var result = _fsrs.Review(FsrsScheduler.NewCard(Start), ReviewRating.Easy, Start);
        Assert.Equal(CardState.Review, result.Card.State);
        Assert.True(result.ScheduledDays >= 1);
        Assert.Equal(FsrsScheduler.DefaultWeights[3], result.Card.Stability, 3);
    }

    [Fact]
    public void Again_on_review_card_is_a_lapse_and_relearns()
    {
        var card = new FsrsCard(CardState.Review, Start.AddDays(10), 10, 5, 0, 5, 0, Start);
        var result = _fsrs.Review(card, ReviewRating.Again, Start.AddDays(10));
        Assert.Equal(CardState.Relearning, result.Card.State);
        Assert.Equal(1, result.Card.Lapses);
        Assert.True(result.Card.Stability < 10, "stability must drop after forgetting");
    }

    [Fact]
    public void Successful_review_increases_stability_and_orders_intervals()
    {
        var card = new FsrsCard(CardState.Review, Start.AddDays(5), 5, 5, 0, 3, 0, Start);
        var now = Start.AddDays(5);
        var hard = _fsrs.Review(card, ReviewRating.Hard, now);
        var good = _fsrs.Review(card, ReviewRating.Good, now);
        var easy = _fsrs.Review(card, ReviewRating.Easy, now);

        Assert.True(good.Card.Stability > card.Stability);
        Assert.True(hard.ScheduledDays <= good.ScheduledDays);
        Assert.True(good.ScheduledDays < easy.ScheduledDays);
        Assert.InRange(good.Retrievability!.Value, 0.85, 0.95); // reviewed on time at ~90% target retention
    }

    [Fact]
    public void Difficulty_stays_within_bounds()
    {
        var card = FsrsScheduler.NewCard(Start);
        var now = Start;
        for (var i = 0; i < 20; i++)
        {
            card = _fsrs.Review(card, ReviewRating.Again, now).Card;
            now = now.AddDays(1);
        }
        Assert.InRange(card.Difficulty, 1, 10);
    }

    [Fact]
    public void Mastery_requires_review_state_and_high_stability()
    {
        Assert.False(FsrsScheduler.IsMastered(CardState.New, 100));
        Assert.False(FsrsScheduler.IsMastered(CardState.Review, 5));
        Assert.True(FsrsScheduler.IsMastered(CardState.Review, 30));
    }
}

public class AnswerGraderTests
{
    [Theory]
    [InlineData("""{"correct":2}""", "2", true)]
    [InlineData("""{"correct":2}""", "1", false)]
    [InlineData("""{"correct":2}""", "\"2\"", true)]
    public void Multiple_choice(string key, string answer, bool expected) =>
        Assert.Equal(expected, AnswerGrader.Grade(QuestionType.MultipleChoice, key, answer).IsCorrect);

    [Fact]
    public void True_false() =>
        Assert.True(AnswerGrader.Grade(QuestionType.TrueFalse, """{"correct":false}""", "false").IsCorrect);

    [Theory]
    [InlineData("\"البناء الضوئي\"", true)]      // exact
    [InlineData("\"البِناء الضَّوئي\"", true)]   // with diacritics
    [InlineData("\"بناء ضوئي\"", false)]
    [InlineData("\"Photosynthesis\"", true)]
    public void Fill_blank_normalises_arabic_and_case(string answer, bool expected) =>
        Assert.Equal(expected, AnswerGrader.Grade(QuestionType.FillBlank, """{"accepted":["البناء الضوئي","photosynthesis"]}""", answer).IsCorrect);

    [Theory]
    [InlineData("\"9.8\"", true)]
    [InlineData("\"9,81 m/s²\"", true)]
    [InlineData("\"٩٫٨\"", true)]
    [InlineData("9.7", false)]
    [InlineData("\"abc\"", false)]
    public void Numerical_uses_relative_tolerance(string answer, bool expected) =>
        Assert.Equal(expected, AnswerGrader.Grade(QuestionType.Numerical, """{"value":9.8,"tolerance":0.01}""", answer).IsCorrect);

    [Fact]
    public void Scientific_notation_is_parsed()
    {
        Assert.True(AnswerGrader.TryParseNumber("6.02×10^23", out var v));
        Assert.Equal(6.02e23, v, 1e18);
    }

    [Fact]
    public void Matching_gives_partial_credit_ordering_does_not()
    {
        var matching = AnswerGrader.Grade(QuestionType.Matching, """{"pairs":[0,1,2,3]}""", "[0,1,3,2]");
        Assert.Equal(0.5, matching.Fraction);
        Assert.False(matching.IsCorrect);

        var ordering = AnswerGrader.Grade(QuestionType.Ordering, """{"order":[2,0,1]}""", "[2,1,0]");
        Assert.Equal(0, ordering.Fraction);
    }

    [Fact]
    public void Open_questions_are_not_graded_deterministically()
    {
        var result = AnswerGrader.Grade(QuestionType.ShortAnswer, """{"modelAnswer":"x"}""", "\"y\"");
        Assert.Null(result.IsCorrect);
        Assert.Equal(GradingMethod.Pending, result.Method);
    }

    [Fact]
    public void Malformed_answer_is_incorrect_not_an_exception() =>
        Assert.False(AnswerGrader.Grade(QuestionType.MultipleChoice, """{"correct":1}""", "{not json").IsCorrect);
}

public class MasteryAndStreakTests
{
    [Fact]
    public void Mastery_moves_toward_evidence()
    {
        var m = 0.0;
        for (var i = 0; i < 5; i++) m = MasteryModel.Update(m, i, success: true, MasteryModel.QuestionWeight);
        Assert.True(m > 0.8);
        var after = MasteryModel.Update(m, 5, success: false, MasteryModel.QuestionWeight);
        Assert.True(after < m);
    }

    [Fact]
    public void Band_requires_minimum_evidence() =>
        Assert.Equal("insufficient", MasteryModel.Band(0.9, 2));

    [Fact]
    public void Streak_counts_consecutive_days_ending_today_or_yesterday()
    {
        var today = new DateOnly(2026, 3, 10);
        var days = new[] { today.AddDays(-1), today.AddDays(-2), today.AddDays(-3), today.AddDays(-6), today.AddDays(-7) };
        var (current, longest) = StreakCalculator.Compute(days, today);
        Assert.Equal(3, current);
        Assert.Equal(3, longest);
        Assert.Equal((0, 0), StreakCalculator.Compute([], today));
        Assert.Equal(0, StreakCalculator.Compute([today.AddDays(-3)], today).Current);
    }
}
