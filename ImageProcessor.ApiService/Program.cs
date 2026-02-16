using System.Text;
using System.Text.Json.Serialization;
using Amazon.S3;
using ImageProcessor.ApiService.Services;
using ImageProcessor.ApiService.Exceptions;
using ImageProcessor.ApiService.Mappings;
using ImageProcessor.ApiService.Messaging;
using ImageProcessor.ApiService.Repositories.Jobs;
using ImageProcessor.ApiService.Repositories.Storage;
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

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();
builder.AddNpgsqlDbContext<AppDbContext>("imageprocessordb");
builder.AddRabbitMQClient("rabbitmq");

builder.Services.AddScoped<JobMapper>();

// JWT Authentication
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
builder.Services.AddScoped<AuthService>();

builder.Services.AddHybridCache();

// Add Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// Add Repositories
builder.Services.AddScoped<IJobRepository, JobRepository>();

// Add S3 Client
var provider = builder.Configuration["Storage:Provider"] ?? "AWS";
builder.Services.AddSingleton<IAmazonS3>(_ =>
{
    var config = builder.Configuration;

    if (provider == "AWS")
    {
        return new AmazonS3Client(
            config["AWS:AccessKeyId"],
            config["AWS:SecretAccessKey"],
            Amazon.RegionEndpoint.GetBySystemName(config["AWS:Region"])
        );    
    } 
    return new AmazonS3Client(
        config["CF:AccessKeyId"],
        config["CF:SecretAccessKey"],
        new AmazonS3Config
        {
            ServiceURL = config["CF:ServiceURL"],
            ForcePathStyle = true,
            AuthenticationRegion = "auto"
        }
    );
});

if (provider == "AWS")
{
    builder.Services.AddScoped<IStorageService, S3Service>();
}
else
{
    builder.Services.AddScoped<IStorageService, R2Service>();    
}

builder.Services.AddScoped<JobService>();

builder.Services.AddScoped<MessagePublisher>();

// Resilience pipeline
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
            Delay = TimeSpan.FromSeconds(1),
        });
});

// Exception Handler
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Apply pending migrations on startup
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

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/", () => "API service is running. Navigate to /weatherforecast to see sample data.");

app.MapDefaultEndpoints();
app.MapControllers();

app.Run();
