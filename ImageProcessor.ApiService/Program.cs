using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Amazon.S3;
using Hangfire;
using Hangfire.PostgreSql;
using ImageProcessor.ApiService.Exceptions;
using ImageProcessor.ApiService.Hangfire;
using ImageProcessor.ApiService.Jobs;
using ImageProcessor.ApiService.Mappings;
using ImageProcessor.ApiService.Messaging;
using ImageProcessor.ApiService.Middleware;
using ImageProcessor.ApiService.OpenApi;
using ImageProcessor.ApiService.Repositories.Jobs;
using ImageProcessor.ApiService.Repositories.Storage;
using ImageProcessor.ApiService.Services;
using ImageProcessor.ApiService.Telemetry;
using ImageProcessor.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using RabbitMQ.Client;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── Aspire ────────────────────────────────────────────────
builder.AddServiceDefaults();
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(ApiTelemetry.ServiceName));
builder.AddNpgsqlDbContext<AppDbContext>("imageprocessordb");
builder.AddRabbitMQClient("rabbitmq");

// ── Authentication & Authorization ────────────────────────
var jwtSecret = builder.Configuration["Jwt:Secret"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });
builder.Services.AddAuthorization();

// ── MVC ───────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "ImageProcessor API";
        document.Info.Description = "Distributed image processing pipeline built with .NET Aspire";
        document.Info.Version = "v1";
        return Task.CompletedTask;
    });
});

// ── Storage ───────────────────────────────────────────────
var provider = builder.Configuration["Storage:Provider"] ?? "AWS";
builder.Services.AddSingleton<IAmazonS3>(_ =>
{
    var config = builder.Configuration;
    if (provider == "AWS")
    {
        return new AmazonS3Client(
            config["AWS:AccessKeyId"],
            config["AWS:SecretAccessKey"],
            Amazon.RegionEndpoint.GetBySystemName(config["AWS:Region"]));
    }
    return new AmazonS3Client(
        config["CF:AccessKeyId"],
        config["CF:SecretAccessKey"],
        new AmazonS3Config
        {
            ServiceURL = config["CF:ServiceURL"],
            ForcePathStyle = true,
            AuthenticationRegion = "auto"
        });
});

if (provider == "AWS")
    builder.Services.AddScoped<IStorageService, S3Service>();
else
    builder.Services.AddScoped<IStorageService, R2Service>();

// ── Application Services ──────────────────────────────────
builder.Services.AddScoped<JobMapper>();
builder.Services.AddScoped<IJobRepository, JobRepository>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<JobService>();
builder.Services.AddScoped<MessagePublisher>();

// ── Caching ───────────────────────────────────────────────
builder.Services.AddHybridCache();

// ── Rate Limiting ─────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            title = "Too Many Requests",
            status = 429,
            detail = "You have exceed the rate limit. Please try again later."
        }, ct);
    };
    
    // Auth endpoints - by IP
    options.AddPolicy("auth", httpContext =>
    {
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 10,
                QueueLimit = 0,
                AutoReplenishment = true
            }
        );
    });
    
    // Upload endpoint - by user ID
    options.AddPolicy("upload", httpContext =>
    {
        return RateLimitPartition.GetTokenBucketLimiter(
            partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
            factory: _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 20,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                TokensPerPeriod = 20,
                AutoReplenishment = true
            }
        );
    });
    
    // Read endpoints - by user ID
    options.AddPolicy("read", httpContext =>
    {
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 100,
                QueueLimit = 0,
                AutoReplenishment = true
            }
        );
    });
});

// ── Resilience ────────────────────────────────────────────
builder.Services.AddResiliencePipeline("storage", pipeline =>
{
    pipeline
        .AddTimeout(TimeSpan.FromSeconds(60))
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(30)
        })
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromSeconds(1)
        });
});

builder.Services.AddResiliencePipeline("messaging", pipeline =>
{
    pipeline
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromMilliseconds(700)
        });
});

// ── Hangfire ──────────────────────────────────────────────
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(opts =>
        opts.UseNpgsqlConnection(builder.Configuration.GetConnectionString("imageprocessordb"))
    )
);
builder.Services.AddHangfireServer();

// ── HSTS ──────────────────────────────────────────────────
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;
});

// ── CORS Policy ───────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("ProductionPolicy", policy =>
    {
        policy.WithOrigins("https://yourdomain.com")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .DisallowCredentials();
    });
});

// ── Exception Handling ────────────────────────────────────
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ─────────────────────────────────────────────────────────
var app = builder.Build();

// ── Startup Tasks ─────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

using (var scope = app.Services.CreateScope())
{
    var connection = scope.ServiceProvider.GetRequiredService<IConnection>();
    await RabbitMQTopology.ConfigureAsync(connection);
}

// ── Cron/Job ────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var jobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    jobManager.AddOrUpdate<CleanupJob>(
        "cleanup-old-jobs",
        job => job.RunAsync(),
        Cron.Daily
    );
}

// ── Middleware ────────────────────────────────────────────
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseCors("ProductionPolicy");

// ── Endpoints ─────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("Image Processor API")
        .AddPreferredSecuritySchemes("Bearer")
    );
}

app.MapDefaultEndpoints();
app.MapControllers();
app.MapGet("/", () => "API service is running.");
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireAuthorizationFilter()]
});

app.Run();
