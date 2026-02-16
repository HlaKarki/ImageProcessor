var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .AddDatabase("imageprocessordb");

var rabbitmq = builder.AddRabbitMQ("rabbitmq");

builder.AddProject<Projects.ImageProcessor_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(postgres)
    .WaitFor(postgres);

builder.AddProject<Projects.ImageProcessor_Worker>("worker")
    .WithReference(rabbitmq)
    .WithReference(postgres)
    .WaitFor(rabbitmq)
    .WaitFor(postgres);

builder.Build().Run();
