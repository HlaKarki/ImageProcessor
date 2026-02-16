using ImageProcessor.Data;
using ImageProcessor.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

builder.AddRabbitMQClient("rabbitmq");
builder.Services.AddHostedService<Worker>();

builder.AddNpgsqlDbContext<AppDbContext>("imageprocessordb");

var host = builder.Build();
host.Run();
