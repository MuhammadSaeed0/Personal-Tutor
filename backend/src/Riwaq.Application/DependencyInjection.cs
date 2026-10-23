using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Riwaq.Application.Features.Account;
using Riwaq.Application.Features.Ai;
using Riwaq.Application.Features.Analytics;
using Riwaq.Application.Features.Books;
using Riwaq.Application.Features.Dashboard;
using Riwaq.Application.Features.Flashcards;
using Riwaq.Application.Features.Notes;
using Riwaq.Application.Features.Planner;
using Riwaq.Application.Features.Profile;
using Riwaq.Application.Features.Progress;
using Riwaq.Application.Features.Quizzes;
using Riwaq.Application.Features.Reader;
using Riwaq.Application.Features.Retrieval;
using Riwaq.Application.Features.Sessions;
using Riwaq.Application.Features.Summaries;
using Riwaq.Application.Features.Tutor;
using Riwaq.Application.Ingestion;

namespace Riwaq.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ProcessingOptions>(configuration.GetSection(ProcessingOptions.Section));

        services.AddScoped<DocumentProcessor>();
        services.AddScoped<BooksService>();
        services.AddScoped<ProfileService>();
        services.AddScoped<AiUsageGuard>();
        services.AddScoped<RetrievalService>();
        services.AddScoped<TutorService>();
        services.AddScoped<SummaryService>();
        services.AddScoped<MasteryService>();
        services.AddScoped<FlashcardService>();
        services.AddScoped<QuizService>();
        services.AddScoped<ReaderService>();
        services.AddScoped<NotesService>();
        services.AddScoped<SessionsService>();
        services.AddScoped<PlannerService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<AccountService>();
        return services;
    }
}
