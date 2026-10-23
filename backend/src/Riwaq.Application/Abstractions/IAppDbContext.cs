using Microsoft.EntityFrameworkCore;
using Riwaq.Domain.Entities;

namespace Riwaq.Application.Abstractions;

/// <summary>
/// The application's view of the database. Implemented by the EF Core context in Infrastructure.
/// Every query on user data must filter by the owning user id.
/// </summary>
public interface IAppDbContext
{
    DbSet<AppUser> Users { get; }
    DbSet<UserPreferences> UserPreferences { get; }
    DbSet<AcademicProfile> AcademicProfiles { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Subject> Subjects { get; }
    DbSet<Exam> Exams { get; }
    DbSet<Book> Books { get; }
    DbSet<DocumentProcessingJob> ProcessingJobs { get; }
    DbSet<DocumentPage> DocumentPages { get; }
    DbSet<Chapter> Chapters { get; }
    DbSet<DocumentChunk> DocumentChunks { get; }
    DbSet<ChunkEmbedding> ChunkEmbeddings { get; }
    DbSet<DocumentWarning> DocumentWarnings { get; }
    DbSet<ReadingProgress> ReadingProgress { get; }
    DbSet<ChapterProgress> ChapterProgress { get; }
    DbSet<Bookmark> Bookmarks { get; }
    DbSet<Highlight> Highlights { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<ConversationMessage> ConversationMessages { get; }
    DbSet<GeneratedSummary> Summaries { get; }
    DbSet<Note> Notes { get; }
    DbSet<Flashcard> Flashcards { get; }
    DbSet<FlashcardReviewLog> FlashcardReviewLogs { get; }
    DbSet<Quiz> Quizzes { get; }
    DbSet<QuizQuestion> QuizQuestions { get; }
    DbSet<QuizAttempt> QuizAttempts { get; }
    DbSet<QuizResponse> QuizResponses { get; }
    DbSet<StudyPlan> StudyPlans { get; }
    DbSet<StudyTask> StudyTasks { get; }
    DbSet<StudySession> StudySessions { get; }
    DbSet<TopicMasteryRecord> TopicMastery { get; }
    DbSet<Achievement> Achievements { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<AiUsageRecord> AiUsage { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
