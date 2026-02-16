using System.Text;
using System.Text.Json;
using ImageProcessor.Contracts.Messages;
using ImageProcessor.Data;
using ImageProcessor.Data.Models.Domain;
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

            logger.LogInformation("Received job {jobId}", message.JobId);

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == message.JobId);
            if (job is not null)
            {
                job.Status = JobStatus.Processing;
                job.StartedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            await channel.BasicAckAsync(ea.DeliveryTag, false);
        };
            
        await channel.BasicConsumeAsync(
            queue: QueueNames.ImageJobs,
            autoAck: false,
            consumer: consumer
        );
        
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
