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
        services.AddSingleton<IOcrEngine, TesseractOcrEngine>();
        services.AddSingleton<IProcessingSignal, ProcessingSignal>();
        services.AddSingleton(TimeProvider.System);

        AddAiProviders(services);

        if (runBackgroundWorker) services.AddHostedService<DocumentProcessingWorker>();
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
                "openaicompatible" when !string.IsNullOrWhiteSpace(options.BaseUrl) =>
                    new OpenAiCompatibleChatModel(sp.GetRequiredService<IHttpClientFactory>().CreateClient("ai"), options),
                _ => new UnconfiguredChatModel()
            };
        });

        services.AddSingleton<IEmbeddingModel>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AiOptions>>().Value.Embeddings;
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("ai");
            return options.Provider.ToLowerInvariant() switch
            {
                "voyage" when !string.IsNullOrWhiteSpace(options.ApiKey) => new VoyageEmbeddingModel(http, options),
                "openaicompatible" => new OpenAiCompatibleEmbeddingModel(http, options),
                _ => new LocalHashingEmbeddingModel()
            };
        });
    }
}
