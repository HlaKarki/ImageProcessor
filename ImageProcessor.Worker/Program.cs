using Amazon.S3;
using ImageProcessor.Data;
using ImageProcessor.Worker;
using ImageProcessor.Worker.Repositories;
using ImageProcessor.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

builder.AddRabbitMQClient("rabbitmq");
builder.AddNpgsqlDbContext<AppDbContext>("imageprocessordb");

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
    builder.Services.AddScoped<IStorageService, S3StorageService>();
}
else
{
    builder.Services.AddScoped<IStorageService, R2StorageService>();    
}

builder.Services.AddScoped<ImageProcessingService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
