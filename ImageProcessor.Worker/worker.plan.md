Step 1 — Create the Worker service project
dotnet new worker -n ImageProcessor.Worker
dotnet sln add ImageProcessor.Worker

Step 2 — Add RabbitMQ to AppHost
- Install Aspire.Hosting.RabbitMQ in AppHost
- Register RabbitMQ in AppHost.cs
- Reference it from both ApiService and Worker

Step 3 — API side (publisher)
- Install Aspire.RabbitMQ.Client in ApiService
- Create a message contract (e.g. ImageJobMessage)
- Create a MessagePublisher service that publishes to RabbitMQ after job creation
- Call it in JobService.CreateAsync after saving to DB

Step 4 — Worker side (consumer)
- Install Aspire.RabbitMQ.Client in Worker
- Set up a background service that listens to the queue
- On message received, update job status to Processing
- (Actual image processing comes in Phase 4)