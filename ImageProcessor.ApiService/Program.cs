using System.Text;
using System.Text.Json.Serialization;
using Amazon.S3;
using ImageProcessor.ApiService.Services;
using ImageProcessor.ApiService.Exceptions;
using ImageProcessor.ApiService.Messaging;
using ImageProcessor.ApiService.Repositories.Jobs;
using ImageProcessor.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RabbitMQ.Client;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();
builder.AddNpgsqlDbContext<AppDbContext>("imageprocessordb");
builder.AddRabbitMQClient("rabbitmq");

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

// Add Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// Add Repositories
builder.Services.AddScoped<IJobRepository, JobRepository>();

// Add S3 Client
builder.Services.AddSingleton<IAmazonS3>(_ =>
{
    var config = builder.Configuration;
    return new AmazonS3Client(
        config["AWS:AccessKeyId"],
        config["AWS:SecretAccessKey"],
        new AmazonS3Config
        {
            ServiceURL = config["AWS:ServiceURL"],
            ForcePathStyle = true
        }
    );
});
builder.Services.AddScoped<S3Service>();

builder.Services.AddScoped<JobService>();

builder.Services.AddScoped<MessagePublisher>();

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
