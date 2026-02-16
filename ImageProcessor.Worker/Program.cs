using ImageProcessor.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.AddRabbitMQClient("rabbitmq");
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
