using Amazon.S3;
using ImageProcessor.Data;
using ImageProcessor.Worker;
using ImageProcessor.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

builder.AddRabbitMQClient("rabbitmq");
builder.AddNpgsqlDbContext<AppDbContext>("imageprocessordb");

builder.Services.AddSingleton<IAmazonS3>(_ =>
{
    var config = builder.Configuration;
    return new AmazonS3Client(
        config["AWS:AccessKeyId"],
        config["AWS:SecretAccessKey"],
        new AmazonS3Config
        {
            ServiceURL = config["AWS:ServiceURL"],
            ForcePathStyle = true,
            AuthenticationRegion = "auto"
        }
    );
});

builder.Services.AddScoped<StorageService>();
builder.Services.AddScoped<ImageProcessingService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
