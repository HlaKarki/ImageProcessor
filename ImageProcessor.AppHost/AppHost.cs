var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .AddDatabase("imageprocessordb");

builder.AddProject<Projects.ImageProcessor_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(postgres)
    .WaitFor(postgres);

builder.Build().Run();
