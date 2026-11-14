using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Riwaq.Application.Abstractions;
using Riwaq.Infrastructure.Ai;
using Riwaq.Infrastructure.Background;
using Riwaq.Infrastructure.Pdf;
using Riwaq.Infrastructure.Persistence;
using Riwaq.Infrastructure.Storage;

namespace Riwaq.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, bool runBackgroundWorker = true)
    {
        var connectionString = configuration.GetConnectionString("Default")
                               ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString, sql =>
        {
            sql.EnableRetryOnFailure(3);
            sql.CommandTimeout(120);
        }));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.Section));
        services.Configure<OcrOptions>(configuration.GetSection(OcrOptions.Section));
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.Section));

        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IPdfExtractor, PdfPigExtractor>();
        services.AddSingleton<TesseractOcrEngine>();
        services.AddSingleton<AiVisionOcrEngine>();
        services.AddSingleton<IOcrEngine, OcrEngineSelector>();
        services.AddSingleton<IImageProcessor, Media.SkiaImageProcessor>();
        services.Configure<PdfRenderOptions>(configuration.GetSection(PdfRenderOptions.Section));
        services.AddSingleton<IPdfRenderer, BrowserPdfRenderer>();
        services.AddHttpClient<IVideoTranscriptSource, Media.YouTubeTranscriptSource>(c => c.Timeout = TimeSpan.FromSeconds(60));
        services.AddSingleton<IProcessingSignal, ProcessingSignal>();
        services.AddSingleton<QuizGenerationQueue>();
        services.AddSingleton<StudyPackSignal>();
        services.AddSingleton<IStudyPackSignal>(sp => sp.GetRequiredService<StudyPackSignal>());
        services.AddSingleton<IQuizGenerationQueue>(sp => sp.GetRequiredService<QuizGenerationQueue>());
        services.AddSingleton(TimeProvider.System);

        AddAiProviders(services);

        if (runBackgroundWorker)
        {
            services.AddHostedService<DocumentProcessingWorker>();
            services.AddHostedService<EmbeddingBackfillWorker>();
            services.AddHostedService<QuizGenerationWorker>();
            services.AddHostedService<StudyPackWorker>();
        }
        return services;
    }

    private static void AddAiProviders(IServiceCollection services)
    {
        services.AddHttpClient("ai", c => c.Timeout = TimeSpan.FromMinutes(5));

        services.AddSingleton<IChatModel>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AiOptions>>().Value.Chat;
            var logger = sp.GetRequiredService<ILoggerFactory>();
            return options.Provider.ToLowerInvariant() switch
            {
                "anthropic" when !string.IsNullOrWhiteSpace(options.ApiKey) =>
                    new AnthropicChatModel(options, logger.CreateLogger<AnthropicChatModel>()),
                "claudecode" => new ClaudeCodeChatModel(options, logger.CreateLogger<ClaudeCodeChatModel>()),
                _ => new UnconfiguredChatModel()
            };
        });

        services.AddSingleton<IEmbeddingModel>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AiOptions>>().Value.Embeddings;
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Riwaq.Embeddings");
            if (!options.Provider.Equals("OnnxE5", StringComparison.OrdinalIgnoreCase)) return new LocalHashingEmbeddingModel();

            var directory = Path.GetFullPath(options.ModelPath);
            if (OnnxE5EmbeddingModel.IsAvailable(directory))
            {
                try { return new OnnxE5EmbeddingModel(directory); }
                catch (Exception ex) { logger.LogError(ex, "The semantic search model could not be loaded; using keyword embeddings."); }
            }
            else
            {
                logger.LogWarning("Semantic search model not found in {Directory}; using keyword embeddings. Run scripts/get-model.ps1 to download it.", directory);
            }
            return new LocalHashingEmbeddingModel();
        });
    }
}
