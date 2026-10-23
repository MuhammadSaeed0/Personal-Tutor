using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Riwaq.Application.Abstractions;
using Riwaq.Infrastructure.Persistence;

namespace Riwaq.Tests.Support;

/// <summary>
/// Hosts the real API against a throw-away SQL Server database. Only the chat model is replaced by a
/// scripted fake; PDF parsing, chunking, embeddings (local), retrieval, grading and scheduling are real.
/// Set RIWAQ_TEST_SQL to point at another SQL Server instance.
/// </summary>
public sealed class RiwaqApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _database = $"Riwaq_Test_{Guid.NewGuid():N}";
    private readonly string _storage = Path.Combine(Path.GetTempPath(), $"riwaq-test-{Guid.NewGuid():N}");

    public FakeChatModel Chat { get; } = new();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private string ConnectionString
    {
        get
        {
            var server = Environment.GetEnvironmentVariable("RIWAQ_TEST_SQL") ?? "Server=localhost;Trusted_Connection=True;TrustServerCertificate=True";
            return $"{server};Database={_database}";
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Storage:RootPath", _storage);
        builder.UseSetting("Ocr:Enabled", "false");
        builder.UseSetting("Ai:Embeddings:Provider", "Local");
        builder.UseSetting("RateLimits:AuthPerMinute", "1000");
        builder.UseSetting("RateLimits:AiPerMinute", "1000");
        builder.UseSetting("RateLimits:UploadsPerHour", "1000");
        builder.UseSetting("RateLimits:GlobalPerMinute", "10000");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IChatModel>();
            services.AddSingleton<IChatModel>(Chat);
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync()
    {
        try
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        }
        catch { /* best effort cleanup */ }
        await base.DisposeAsync();
        try { Directory.Delete(_storage, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Registers a fresh student and returns an authenticated client.</summary>
    public async Task<HttpClient> CreateStudentAsync(string? email = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = email ?? $"student-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd-123",
            displayName = "طالب تجريبي",
            uiLanguage = "ar"
        });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        return client;
    }
}

internal static class ServiceCollectionTestExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(T)).ToList()) services.Remove(descriptor);
    }
}
