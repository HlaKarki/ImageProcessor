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
using Microsoft.Extensions.DependencyInjection;
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
        await using var imageChannel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await using var aiChannel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await imageChannel.QueueDeclareAsync(
            queue: QueueNames.ImageJobs,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken
        );

        await aiChannel.QueueDeclareAsync(
            queue: QueueNames.ImageAiJobs,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken
        );

        var imageConsumer = new AsyncEventingBasicConsumer(imageChannel);
        imageConsumer.ReceivedAsync += async (_, ea) => await HandleImageJobAsync(imageChannel, ea, stoppingToken);

        var aiConsumer = new AsyncEventingBasicConsumer(aiChannel);
        aiConsumer.ReceivedAsync += async (_, ea) => await HandleAiJobAsync(aiChannel, ea, stoppingToken);
            
        await imageChannel.BasicConsumeAsync(
            queue: QueueNames.ImageJobs,
            autoAck: false,
            consumer: imageConsumer
        );

        await aiChannel.BasicConsumeAsync(
            queue: QueueNames.ImageAiJobs,
            autoAck: false,
            consumer: aiConsumer
        );
        
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleImageJobAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken cancellationToken)
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
            logger.LogInformation("Received image job {JobId}", message.JobId);

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var processor = scope.ServiceProvider.GetRequiredService<ImageProcessingService>();

            var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == message.JobId, cancellationToken);
            if (job is null)
            {
                logger.LogInformation("Job {JobId} not found, discarding", message.JobId);
                await channel.BasicNackAsync(ea.DeliveryTag, false, false);
                return;
            }

            job.Status = JobStatus.Processing;
            job.StartedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

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
                job.ErrorMessage = null;
                await db.SaveChangesAsync(cancellationToken);

                await QueueAiAnalysisAsync(channel, job, optimizedUrls, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);

                stopwatch.Stop();
                WorkerTelemetry.JobsProcessed.Add(1);
                WorkerTelemetry.ProcessingDuration.Record(stopwatch.Elapsed.TotalMilliseconds);

                logger.LogInformation("Image job {JobId} completed in {ElapsedMs}ms",
                    message.JobId, stopwatch.ElapsedMilliseconds);
                await channel.BasicAckAsync(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                WorkerTelemetry.JobsFailed.Add(1);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

                logger.LogError(ex, "Image job {JobId} failed after {ElapsedMs}ms", message.JobId,
                    stopwatch.ElapsedMilliseconds);

                job.Status = JobStatus.Error;
                job.ErrorMessage = ex.Message;
                job.RetryCount++;
                job.AiStatus = JobAiStatus.Skipped;
                job.AiErrorMessage = "Skipped due to image processing failure.";
                await db.SaveChangesAsync(cancellationToken);
                await channel.BasicNackAsync(ea.DeliveryTag, false, false);
            }
        }
    }

    private async Task QueueAiAnalysisAsync(
        IChannel channel,
        Job job,
        IReadOnlyDictionary<string, string> optimizedUrls,
        CancellationToken cancellationToken)
    {
        try
        {
            optimizedUrls.TryGetValue("webp", out var sourceImageUrl);
            sourceImageUrl ??= job.OriginalUrl;
            var aiMessage = new ImageAiJobMessage(job.Id, job.UserId, sourceImageUrl);
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(aiMessage));

            await channel.BasicPublishAsync(
                exchange: "",
                routingKey: QueueNames.ImageAiJobs,
                mandatory: false,
                basicProperties: new BasicProperties { Persistent = true },
                body: body,
                cancellationToken: cancellationToken
            );

            job.AiStatus = JobAiStatus.Pending;
            job.AiErrorMessage = null;
            logger.LogInformation("Queued AI analysis for job {JobId}", job.Id);
        }
        catch (Exception ex)
        {
            job.AiStatus = JobAiStatus.Error;
            job.AiErrorMessage = $"Failed to queue AI stage: {ex.Message}";
            job.AiRetryCount++;
            logger.LogError(ex, "Failed to queue AI analysis for job {JobId}", job.Id);
        }
    }

    private async Task HandleAiJobAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetString(ea.Body.ToArray());
        var message = JsonSerializer.Deserialize<ImageAiJobMessage>(body);

        if (message is null)
        {
            await channel.BasicNackAsync(ea.DeliveryTag, false, false);
            return;
        }

        using var activity = WorkerTelemetry.ActivitySource.StartActivity("job.ai_analyze");
        activity?.SetTag("job.id", message.JobId);
        activity?.SetTag("user.id", message.UserId);

        using (logger.BeginScope(new Dictionary<string, object>
               {
                   ["JobId"] = message.JobId,
                   ["UserId"] = message.UserId,
                   ["Queue"] = QueueNames.ImageAiJobs
               }))
        {
            logger.LogInformation("Received AI job for {JobId}", message.JobId);

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
            var analysis = scope.ServiceProvider.GetRequiredService<ImageAnalysisService>();

            var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == message.JobId, cancellationToken);
            if (job is null)
            {
                logger.LogInformation("Job {JobId} not found for AI stage, discarding", message.JobId);
                await channel.BasicNackAsync(ea.DeliveryTag, false, false);
                return;
            }

            job.AiStatus = JobAiStatus.Processing;
            job.AiStartedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var key = storage.ExtractKey(message.SourceImageUrl);
                await using var imageStream = await storage.DownloadAsync(key);

                var result = await analysis.AnalyzeAsync(imageStream, cancellationToken);
                job.AiAnalysis = JsonDocument.Parse(JsonSerializer.Serialize(result));
                job.AiStatus = JobAiStatus.Completed;
                job.AiCompletedAt = DateTime.UtcNow;
                job.AiErrorMessage = null;
                await db.SaveChangesAsync(cancellationToken);

                stopwatch.Stop();
                WorkerTelemetry.AiJobsProcessed.Add(1);
                WorkerTelemetry.AiProcessingDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
                logger.LogInformation("AI job {JobId} completed in {ElapsedMs}ms",
                    message.JobId, stopwatch.ElapsedMilliseconds);

                await channel.BasicAckAsync(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                WorkerTelemetry.AiJobsFailed.Add(1);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogError(ex, "AI job {JobId} failed after {ElapsedMs}ms",
                    message.JobId, stopwatch.ElapsedMilliseconds);

                job.AiStatus = JobAiStatus.Error;
                job.AiErrorMessage = ex.Message;
                job.AiRetryCount++;
                await db.SaveChangesAsync(cancellationToken);

                await channel.BasicNackAsync(ea.DeliveryTag, false, false);
            }
        }
    }
}
