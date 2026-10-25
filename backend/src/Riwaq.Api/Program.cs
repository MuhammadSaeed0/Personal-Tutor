using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Riwaq.Api.Auth;
using Riwaq.Api.Infrastructure;
using Riwaq.Application;
using Riwaq.Application.Abstractions;
using Riwaq.Domain.Entities;
using Riwaq.Infrastructure;
using Riwaq.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---- Application & infrastructure -------------------------------------------------------------
builder.Services.AddApplication(config);
builder.Services.AddInfrastructure(config, runBackgroundWorker: config.GetValue("Processing:RunWorker", true));
builder.Services.AddMemoryCache(o => o.SizeLimit = config.GetValue<long>("Cache:SizeLimit", 512));

// ---- Identity & authentication ----------------------------------------------------------------
builder.Services.AddIdentityCore<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireDigit = true;
        o.Lockout.MaxFailedAccessAttempts = 8;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
builder.Services.Configure<EmailOptions>(config.GetSection(EmailOptions.Section));
builder.Services.AddSingleton<SigningKeyProvider>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>, SigningKeyProvider>((o, jwt, keys) =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Value.Issuer,
            ValidAudience = jwt.Value.Audience,
            IssuerSigningKey = keys.Key,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name"
        };
        // SignalR sends the token in the query string because WebSockets cannot carry headers.
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// ---- API surface ------------------------------------------------------------------------------
builder.Services.AddControllers(o => o.Filters.Add(new ProducesAttribute("application/json")))
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    });
builder.Services.Configure<ApiBehaviorOptions>(o =>
{
    o.InvalidModelStateResponseFactory = ctx =>
    {
        var problem = new ValidationProblemDetails(ctx.ModelState) { Status = 400, Title = "Validation failed" };
        problem.Extensions["code"] = "validation_failed";
        return new BadRequestObjectResult(problem);
    };
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();

builder.Services.AddSignalR().AddJsonProtocol(o =>
    o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));
builder.Services.AddSingleton<IUserIdProvider, SubUserIdProvider>();
builder.Services.AddSingleton<IRealtimeNotifier, SignalRNotifier>();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    static string UserOrIp(HttpContext ctx) => ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon";

    o.AddPolicy(RateLimitPolicies.Auth, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("RateLimits:AuthPerMinute", 20), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy(RateLimitPolicies.Ai, ctx => RateLimitPartition.GetFixedWindowLimiter(UserOrIp(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("RateLimits:AiPerMinute", 20), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy(RateLimitPolicies.Upload, ctx => RateLimitPartition.GetFixedWindowLimiter(UserOrIp(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("RateLimits:UploadsPerHour", 30), Window = TimeSpan.FromHours(1) }));
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(UserOrIp(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("RateLimits:GlobalPerMinute", 600), Window = TimeSpan.FromMinutes(1) }));
});

var allowedOrigins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (allowedOrigins.Length > 0)
        p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

// Behind a TLS-terminating proxy (nginx, Azure Front Door...), trust X-Forwarded-* so HTTPS and client IPs are correct.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "Riwaq Learning API", Version = "v1", Description = "Backend API for the Riwaq AI study platform." });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "JWT access token from /api/auth/login"
    });
    o.AddSecurityRequirement(doc => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", doc)] = [] });
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
    o.MultipartBodyLengthLimit = config.GetValue("Processing:MaxFileSizeMb", 250) * 1024L * 1024L + 1024 * 1024);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = config.GetValue("Processing:MaxFileSizeMb", 250) * 1024L * 1024L + 1024 * 1024);

var app = builder.Build();

// ---- Database -----------------------------------------------------------------------------------
if (config.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

// ---- Pipeline -----------------------------------------------------------------------------------
if (config.GetValue("ForwardedHeaders:Enabled", false)) app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment() || config.GetValue("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.DocumentTitle = "Riwaq API");
}

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHub<RealtimeHub>("/hubs/realtime");
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
app.MapGet("/api/system/status", (IChatModel chat, IEmbeddingModel embeddings, IOcrEngine ocr) => Results.Ok(new
{
    ai = new { configured = chat.IsConfigured, provider = chat.Provider, model = chat.IsConfigured ? chat.ModelName : null },
    embeddings = new { model = embeddings.ModelName, semantic = embeddings.IsSemantic },
    ocr = new { available = ocr.IsAvailable, engine = ocr.Name }
})).AllowAnonymous();

// SPA fallback when the Angular build is served from wwwroot (single-process production build).
if (Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot")))
    app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

public partial class Program;
