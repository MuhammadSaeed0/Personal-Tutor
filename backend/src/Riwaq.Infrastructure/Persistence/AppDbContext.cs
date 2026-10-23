using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Riwaq.Application.Abstractions;
using Riwaq.Domain.Entities;

namespace Riwaq.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IAppDbContext
{
    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();
    public DbSet<AcademicProfile> AcademicProfiles => Set<AcademicProfile>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<DocumentProcessingJob> ProcessingJobs => Set<DocumentProcessingJob>();
    public DbSet<DocumentPage> DocumentPages => Set<DocumentPage>();
    public DbSet<RecognizedPage> RecognizedPages => Set<RecognizedPage>();
    public DbSet<Chapter> Chapters => Set<Chapter>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<ChunkEmbedding> ChunkEmbeddings => Set<ChunkEmbedding>();
    public DbSet<DocumentWarning> DocumentWarnings => Set<DocumentWarning>();
    public DbSet<ReadingProgress> ReadingProgress => Set<ReadingProgress>();
    public DbSet<ChapterProgress> ChapterProgress => Set<ChapterProgress>();
    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();
    public DbSet<Highlight> Highlights => Set<Highlight>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
    public DbSet<GeneratedSummary> Summaries => Set<GeneratedSummary>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<Flashcard> Flashcards => Set<Flashcard>();
    public DbSet<FlashcardReviewLog> FlashcardReviewLogs => Set<FlashcardReviewLog>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<QuizResponse> QuizResponses => Set<QuizResponse>();
    public DbSet<StudyPlan> StudyPlans => Set<StudyPlan>();
    public DbSet<StudyTask> StudyTasks => Set<StudyTask>();
    public DbSet<StudySession> StudySessions => Set<StudySession>();
    public DbSet<TopicMasteryRecord> TopicMastery => Set<TopicMasteryRecord>();
    public DbSet<Achievement> Achievements => Set<Achievement>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AiUsageRecord> AiUsage => Set<AiUsageRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // All timestamps are UTC; store with sufficient precision.
        builder.Properties<DateTime>().HavePrecision(3);
        builder.Properties<string>().HaveMaxLength(400);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        ConfigureIdentity(b);
        ConfigureBooks(b);
        ConfigureReading(b);
        ConfigureLearning(b);
        ConfigureQuizzes(b);
        ConfigurePlanning(b);
        SubjectSeed.Apply(b);
    }

    private static void ConfigureIdentity(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.HasOne(x => x.Preferences).WithOne().HasForeignKey<UserPreferences>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AcademicProfile).WithOne().HasForeignKey<AcademicProfile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UserPreferences>(e =>
        {
            e.HasKey(x => x.UserId);
            e.Property(x => x.UiLanguage).HasMaxLength(5);
            e.Property(x => x.ExplanationLanguage).HasMaxLength(5);
            e.Property(x => x.Theme).HasMaxLength(10);
            e.Property(x => x.TeachingStyle).HasMaxLength(20);
        });

        b.Entity<AcademicProfile>(e =>
        {
            e.HasKey(x => x.UserId);
            e.Property(x => x.Grade).HasMaxLength(60);
            e.Property(x => x.Curriculum).HasMaxLength(120);
            e.Property(x => x.AcademicYear).HasMaxLength(20);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.ReplacedByHash).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(300);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Subject>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.Color).HasMaxLength(9);
            e.Property(x => x.Icon).HasMaxLength(40);
            e.Property(x => x.ContentLanguage).HasMaxLength(5);
        });

        b.Entity<Exam>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Date });
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureBooks(ModelBuilder b)
    {
        b.Entity<Book>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.SubjectId });
            e.HasIndex(x => new { x.UserId, x.Sha256 }).IsUnique();
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.StorageKey).HasMaxLength(300);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.DetectedLanguage).HasMaxLength(10);
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Chapters).WithOne().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DocumentProcessingJob>(e =>
        {
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasIndex(x => x.BookId);
            e.Property(x => x.ErrorMessage).HasMaxLength(1000);
            e.HasOne(x => x.Book).WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DocumentPage>(e =>
        {
            e.HasIndex(x => new { x.BookId, x.PageNumber }).IsUnique();
            e.Property(x => x.Text).HasColumnType("nvarchar(max)");
            e.HasOne<Book>().WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RecognizedPage>(e =>
        {
            e.HasKey(x => new { x.BookId, x.PageNumber });
            e.Property(x => x.Text).HasColumnType("nvarchar(max)");
            e.HasOne<Book>().WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Chapter>(e =>
        {
            e.HasIndex(x => new { x.BookId, x.Order });
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.DetectedTitle).HasMaxLength(300);
            e.HasOne<Chapter>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<DocumentChunk>(e =>
        {
            e.HasIndex(x => new { x.BookId, x.Ordinal });
            e.HasIndex(x => new { x.BookId, x.PageStart });
            e.Property(x => x.Text).HasColumnType("nvarchar(max)");
            e.Property(x => x.SectionTitle).HasMaxLength(300);
            e.Property(x => x.ContentHash).HasMaxLength(64);
            e.HasOne<Book>().WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ChunkEmbedding>(e =>
        {
            e.HasIndex(x => new { x.ChunkId, x.Model }).IsUnique();
            e.HasIndex(x => new { x.BookId, x.Model });
            e.HasIndex(x => new { x.ContentHash, x.Model });
            e.Property(x => x.Model).HasMaxLength(120);
            e.Property(x => x.ContentHash).HasMaxLength(64);
            e.Property(x => x.Vector).HasColumnType("varbinary(max)");
            e.HasOne<DocumentChunk>().WithMany().HasForeignKey(x => x.ChunkId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DocumentWarning>(e =>
        {
            e.HasIndex(x => x.BookId);
            e.Property(x => x.Code).HasMaxLength(60);
            e.Property(x => x.Message).HasMaxLength(1000);
            e.HasOne<Book>().WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureReading(ModelBuilder b)
    {
        b.Entity<ReadingProgress>(e =>
        {
            e.HasKey(x => new { x.UserId, x.BookId });
            e.HasIndex(x => new { x.UserId, x.UpdatedAt });
            e.Property(x => x.ReadPages).HasColumnType("varbinary(max)");
            e.HasOne<Book>().WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ChapterProgress>(e =>
        {
            e.HasKey(x => new { x.UserId, x.ChapterId });
            e.HasIndex(x => new { x.UserId, x.BookId });
            e.HasOne<Chapter>().WithMany().HasForeignKey(x => x.ChapterId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Bookmark>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.BookId, x.PageNumber }).IsUnique();
            e.Property(x => x.Label).HasMaxLength(200);
            e.HasOne<Book>().WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Highlight>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.BookId, x.PageNumber });
            e.Property(x => x.Text).HasMaxLength(4000);
            e.Property(x => x.Color).HasMaxLength(20);
            e.Property(x => x.RectsJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.Annotation).HasMaxLength(4000);
            e.HasOne<Book>().WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureLearning(ModelBuilder b)
    {
        b.Entity<Conversation>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.BookId, x.UpdatedAt });
            e.Property(x => x.Title).HasMaxLength(200);
            e.HasOne(x => x.Book).WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ConversationMessage>(e =>
        {
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            e.Property(x => x.Content).HasColumnType("nvarchar(max)");
            e.Property(x => x.SelectedText).HasMaxLength(8000);
            e.Property(x => x.Action).HasMaxLength(40);
            e.Property(x => x.Model).HasMaxLength(120);
            e.Property(x => x.ErrorCode).HasMaxLength(60);
            e.OwnsMany(x => x.Citations, o => o.ToJson());
        });

        b.Entity<GeneratedSummary>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.BookId, x.CreatedAt });
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Content).HasColumnType("nvarchar(max)");
            e.Property(x => x.Language).HasMaxLength(5);
            e.Property(x => x.Model).HasMaxLength(120);
            e.HasOne(x => x.Book).WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.Cascade);
            e.OwnsMany(x => x.Citations, o => o.ToJson());
        });

        b.Entity<Note>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.UpdatedAt });
            e.HasIndex(x => new { x.UserId, x.BookId });
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Content).HasColumnType("nvarchar(max)");
            e.HasOne<Book>().WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.SetNull);
            e.OwnsMany(x => x.Sources, o => o.ToJson());
        });

        b.Entity<Flashcard>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Due });
            e.HasIndex(x => new { x.UserId, x.SubjectId });
            e.Property(x => x.Front).HasMaxLength(2000);
            e.Property(x => x.Back).HasMaxLength(4000);
            e.HasOne<Book>().WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<FlashcardReviewLog>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.ReviewedAt });
            e.HasIndex(x => x.FlashcardId);
            e.HasOne<Flashcard>().WithMany().HasForeignKey(x => x.FlashcardId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureQuizzes(ModelBuilder b)
    {
        b.Entity<Quiz>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Language).HasMaxLength(5);
            e.Property(x => x.ErrorMessage).HasMaxLength(1000);
            e.Property(x => x.Model).HasMaxLength(120);
            e.HasOne(x => x.Book).WithMany().HasForeignKey(x => x.BookId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Questions).WithOne().HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<QuizQuestion>(e =>
        {
            e.HasIndex(x => new { x.QuizId, x.Ordinal });
            e.Property(x => x.Prompt).HasMaxLength(4000);
            e.Property(x => x.OptionsJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.AnswerKeyJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.Explanation).HasColumnType("nvarchar(max)");
            e.Property(x => x.Hint).HasMaxLength(1000);
            e.Property(x => x.Topic).HasMaxLength(200);
            e.Property(x => x.SourceQuote).HasMaxLength(1000);
        });

        b.Entity<QuizAttempt>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.SubmittedAt });
            e.HasOne(x => x.Quiz).WithMany().HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Responses).WithOne().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<QuizResponse>(e =>
        {
            e.HasIndex(x => new { x.AttemptId, x.QuestionId }).IsUnique();
            e.Property(x => x.AnswerJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.Feedback).HasMaxLength(4000);
        });
    }

    private static void ConfigurePlanning(ModelBuilder b)
    {
        b.Entity<StudyPlan>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.GeneratedAt });
            e.HasMany(x => x.Tasks).WithOne().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<StudyTask>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Date });
            e.Property(x => x.ReasonCode).HasMaxLength(60);
            e.Property(x => x.Topic).HasMaxLength(300);
        });

        b.Entity<StudySession>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.StartedAt });
            e.Property(x => x.Goal).HasMaxLength(500);
            e.Property(x => x.Notes).HasMaxLength(4000);
        });

        b.Entity<TopicMasteryRecord>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.TopicKey }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.SubjectId });
            e.Property(x => x.TopicKey).HasMaxLength(200);
            e.Property(x => x.TopicLabel).HasMaxLength(300);
        });

        b.Entity<Achievement>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(60);
        });

        b.Entity<Notification>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt });
            e.Property(x => x.Type).HasMaxLength(60);
            e.Property(x => x.EntityId).HasMaxLength(60);
            e.Property(x => x.Data).HasMaxLength(2000);
        });

        b.Entity<AiUsageRecord>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.Property(x => x.Feature).HasMaxLength(40);
            e.Property(x => x.Model).HasMaxLength(120);
        });
    }
}
