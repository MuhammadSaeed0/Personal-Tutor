using Microsoft.EntityFrameworkCore;
using Riwaq.Domain.Entities;

namespace Riwaq.Infrastructure.Persistence;

/// <summary>The seven core subjects. Icons are Lucide icon names rendered by the frontend.</summary>
internal static class SubjectSeed
{
    public static readonly Subject[] Subjects =
    [
        new() { Id = 1, Code = "arabic", NameAr = "اللغة العربية", NameEn = "Arabic", Color = "#A16207", Icon = "scroll-text", SortOrder = 1, ContentLanguage = "ar" },
        new() { Id = 2, Code = "english", NameAr = "اللغة الإنجليزية", NameEn = "English", Color = "#2563EB", Icon = "book-a", SortOrder = 2, ContentLanguage = "en" },
        new() { Id = 3, Code = "french", NameAr = "اللغة الفرنسية", NameEn = "French", Color = "#6D28D9", Icon = "languages", SortOrder = 3, ContentLanguage = "fr" },
        new() { Id = 4, Code = "physics", NameAr = "الفيزياء", NameEn = "Physics", Color = "#0369A1", Icon = "atom", SortOrder = 4, ContentLanguage = "ar" },
        new() { Id = 5, Code = "chemistry", NameAr = "الكيمياء", NameEn = "Chemistry", Color = "#0F766E", Icon = "flask-conical", SortOrder = 5, ContentLanguage = "ar" },
        new() { Id = 6, Code = "biology", NameAr = "الأحياء", NameEn = "Biology", Color = "#4D7C0F", Icon = "dna", SortOrder = 6, ContentLanguage = "ar" },
        new() { Id = 7, Code = "geography", NameAr = "الجغرافيا", NameEn = "Geography", Color = "#B4532A", Icon = "earth", SortOrder = 7, ContentLanguage = "ar" },
    ];

    public static void Apply(ModelBuilder b) => b.Entity<Subject>().HasData(Subjects);
}
