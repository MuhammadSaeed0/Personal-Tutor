using Riwaq.Application.Text;

namespace Riwaq.Tests.Unit;

public class OcrTextRepairTests
{
    /// <summary>A book whose correct words recur, as they do across a real textbook.</summary>
    private static readonly string[] Book =
    [
        .. Enumerable.Repeat("تعريف المعدن: للمعادن تركيب كيميائي محدد. يكتب الطالب تعريف المعدن. المعلمون يشرحون للمعادن.", 6),
        .. Enumerable.Repeat("الحرارة غير الحضارة.", 6)
    ];

    private static string Repair(string page) => OcrTextRepair.Repair(page, OcrTextRepair.BuildVocabulary([.. Book, page]));

    [Fact]
    public void Words_misread_in_one_interior_letter_take_the_books_spelling()
    {
        Assert.Equal("تعريف البلوري للمعادن.", Repair("تعزيف البلوري للقعادن."));
    }

    [Fact]
    public void Grammatical_variants_are_never_rewritten()
    {
        // First letter (person) and last letters (number, case) are where valid Arabic words differ.
        Assert.Equal("تكتب الطالبة.", Repair("تكتب الطالبة."));
        Assert.Equal("رأيت المعلمين.", Repair("رأيت المعلمين."));
    }

    [Fact]
    public void Ambiguous_fixes_are_left_alone()
    {
        // "الحزارة" is one letter from both "الحرارة" and "الحضارة", each used as often.
        Assert.Contains("الحزارة", Repair("الحزارة مهمة."));
    }

    [Fact]
    public void Noise_symbols_and_fragment_runs_are_removed_but_formulas_stay()
    {
        var text = Repair("© المعدن | مادة صلبة د سي ري رو رمس رمم رم رس dla_c] pH و Na و F = m a + 2");

        Assert.DoesNotContain("©", text);
        Assert.DoesNotContain("|", text);
        Assert.DoesNotContain("رمس", text);
        Assert.DoesNotContain("dla_c", text);
        Assert.Contains("المعدن مادة صلبة", text);
        Assert.Contains("pH و Na و F = m a + 2", text);
    }

    [Fact]
    public void Direction_marks_inside_words_are_dropped()
    {
        Assert.Equal("ترسم همزة القطع", Repair("ترسم‎ همزة‏ القطع"));
    }
}
