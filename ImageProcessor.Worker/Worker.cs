using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ImageProcessor.Contracts.Messages;
using ImageProcessor.Data;
using ImageProcessor.Data.Models.Domain;
using ImageProcessor.Worker.Repositories;
using ImageProcessor.Worker.Services;
using ImageProcessor.Worker.Telemetry;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ImageProcessor.Worker;

public class Worker(
    IConnection connection,
    IServiceScopeFactory scopeFactory,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            queue: QueueNames.ImageJobs,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken
        );

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var body = Encoding.UTF8.GetString(ea.Body.ToArray());
            var message = JsonSerializer.Deserialize<ImageJobMessage>(body);

            if (message is null)
            {
                await channel.BasicNackAsync(ea.DeliveryTag, false, false);
                return;
            }

            using var activity = WorkerTelemetry.ActivitySource.StartActivity("job.process");
            activity?.SetTag("job.id", message.JobId);
            activity?.SetTag("user.id", message.UserId);

            using (logger.BeginScope(new Dictionary<string, object>
                   {
                       ["JobId"] = message.JobId,
                       ["UserId"] = message.UserId,
                   }))
            {
                logger.LogInformation("Received job {jobId}", message.JobId);

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
                var processor = scope.ServiceProvider.GetRequiredService<ImageProcessingService>();

                var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == message.JobId);
                if (job is null)
                {
                    logger.LogInformation("Job {jobId} not found, discarding", message.JobId);
                    await channel.BasicNackAsync(ea.DeliveryTag, false, false);
                    return;
                }
                
                job.Status = JobStatus.Processing;
                job.StartedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var originalKey = storage.ExtractKey(message.OriginalUrl);
                    await using var imageStream = await storage.DownloadAsync(originalKey);

                    var result = await processor.ProcessAsync(imageStream, job.FileSize);

                    var thumbUrls = new Dictionary<string, string>();
                    foreach (var (name, stream) in result.Thumbnails)
                    {
                        await using (stream)
                        {
                            var key = $"processed/{message.UserId}/{message.JobId}/{name}.webp";
                            thumbUrls[name] = await storage.UploadAsync(stream, key, "image/webp");
                        }
                    }

                    var optimizedUrls = new Dictionary<string, string>();
                    foreach (var (format, stream) in result.Optimized)
                    {
                        await using (stream)
                        {
                            var key = $"processed/{message.UserId}/{message.JobId}/optimized.{format}";
                            optimizedUrls[format] = await storage.UploadAsync(stream, key, "image/webp");
                        }
                    }

                    job.Thumbnails = JsonDocument.Parse(JsonSerializer.Serialize(thumbUrls));
                    job.Optimized = JsonDocument.Parse(JsonSerializer.Serialize(optimizedUrls));
                    job.Metadata = JsonDocument.Parse(JsonSerializer.Serialize(result.Metadata));
                    job.Status = JobStatus.Completed;
                    job.CompletedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();

                    stopwatch.Stop();
                    WorkerTelemetry.JobsProcessed.Add(1);
                    WorkerTelemetry.ProcessingDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
                    
                    logger.LogInformation("Job {JobId} completed in {ElapsedMs}ms", 
                        message.JobId, stopwatch.ElapsedMilliseconds);
                    await channel.BasicAckAsync(ea.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    WorkerTelemetry.JobsFailed.Add(1);
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    
                    logger.LogError(ex, "Job {JobId} failed after {ElapsedMs}ms", message.JobId,
                        stopwatch.ElapsedMilliseconds);
                    
                    job.Status = JobStatus.Error;
                    job.ErrorMessage = ex.Message;
                    job.RetryCount++;
                    await db.SaveChangesAsync();
                    await channel.BasicNackAsync(ea.DeliveryTag, false, false);
                }
            }
        };
            
        await channel.BasicConsumeAsync(
            queue: QueueNames.ImageJobs,
            autoAck: false,
            consumer: consumer
        );
        
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
