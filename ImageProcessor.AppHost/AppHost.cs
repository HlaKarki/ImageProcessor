var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .AddDatabase("imageprocessordb");

var rabbitmq = builder.AddRabbitMQ("rabbitmq").WithDataVolume();

builder.AddProject<Projects.ImageProcessor_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(rabbitmq)
    .WithReference(postgres)
    .WaitFor(rabbitmq)
    .WaitFor(postgres);

builder.AddProject<Projects.ImageProcessor_Worker>("worker")
    .WithReference(rabbitmq)
    .WithReference(postgres)
    .WaitFor(rabbitmq)
    .WaitFor(postgres);

builder.Build().Run();
