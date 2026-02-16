using Amazon.S3;
using ImageProcessor.Data;
using ImageProcessor.Worker;
using ImageProcessor.Worker.Repositories;
using ImageProcessor.Worker.Services;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

var builder = Host.CreateApplicationBuilder(args);

// ── Aspire ────────────────────────────────────────────────
builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<AppDbContext>("imageprocessordb");
builder.AddRabbitMQClient("rabbitmq");

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
    builder.Services.AddScoped<IStorageService, S3StorageService>();
else
    builder.Services.AddScoped<IStorageService, R2StorageService>();

// ── Application Services ──────────────────────────────────
builder.Services.AddScoped<ImageProcessingService>();
builder.Services.AddHostedService<Worker>();

// ── Resilience ────────────────────────────────────────────
builder.Services.AddResiliencePipeline("storage", pipeline =>
{
    pipeline
        .AddTimeout(TimeSpan.FromSeconds(30))
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

var host = builder.Build();
host.Run();
