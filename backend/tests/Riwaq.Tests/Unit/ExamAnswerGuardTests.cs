using Riwaq.Application.Features.Tutor;
using Riwaq.Domain;
using Riwaq.Domain.Entities;

namespace Riwaq.Tests.Unit;

public class ExamAnswerGuardTests
{
    private static QuizQuestion Q(QuestionType type, string prompt, string options, string key) =>
        new() { Id = Guid.NewGuid(), Type = type, Prompt = prompt, OptionsJson = options, AnswerKeyJson = key };

    private static readonly QuizQuestion Choice = Q(QuestionType.MultipleChoice, "ما وحدة قياس القوة؟",
        """{"choices":["الجول","النيوتن","الواط","الباسكال"]}""", """{"correct":1}""");
    private static readonly QuizQuestion Numeric = Q(QuestionType.Numerical, "جسم كتلته 4 kg وعجلته 5 m/s². احسب القوة.",
        "{}", """{"value":20,"unit":"N","tolerance":0.1}""");
    private static readonly QuizQuestion Statement = Q(QuestionType.TrueFalse, "الطاقة تفنى.", "{}", """{"correct":false}""");

    [Theory]
    [InlineData("الإجابة هي النيوتن لأنه وحدة القوة.")]
    [InlineData("وحدة القوة هي النِّيُوتُن.")]                      // diacritics do not hide it
    public void The_correct_choice_text_is_caught(string reply) =>
        Assert.True(ExamAnswerGuard.Reveals(reply, Choice, [Choice]));

    [Fact]
    public void Explaining_a_wrong_choice_is_allowed() =>
        Assert.False(ExamAnswerGuard.Reveals("الجول وحدة قياس الطاقة والشغل.", Choice, [Choice]));

    [Theory]
    [InlineData("القوة = 4 × 5 = 20 N", true)]
    [InlineData("القوة = ٢٠ نيوتن", true)]                         // Arabic-Indic digits
    [InlineData("استخدم قانون نيوتن الثاني: القوة تساوي الكتلة في العجلة، والكتلة هنا 4 kg.", false)] // a given value is fine
    public void The_numeric_result_is_caught_but_given_values_are_not(string reply, bool reveals) =>
        Assert.Equal(reveals, ExamAnswerGuard.Reveals(reply, Numeric, [Numeric]));

    [Theory]
    [InlineData("العبارة خاطئة لأن الطاقة لا تفنى.", true)]
    [InlineData("The statement is false.", true)]
    [InlineData("قانون حفظ الطاقة يقول إن الطاقة تتحول من صورة لأخرى.", false)]
    public void A_true_false_verdict_is_caught(string reply, bool reveals) =>
        Assert.Equal(reveals, ExamAnswerGuard.Reveals(reply, Statement, [Statement]));

    [Fact]
    public void Another_question_of_the_exam_is_also_protected() =>
        Assert.True(ExamAnswerGuard.Reveals("القوة = 20 N", Choice, [Choice, Numeric]));
}
