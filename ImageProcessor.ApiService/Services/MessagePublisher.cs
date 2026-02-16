using System.Text;
using System.Text.Json;
using ImageProcessor.Contracts.Messages;
using Polly.Registry;
using RabbitMQ.Client;

namespace ImageProcessor.ApiService.Services;

public class MessagePublisher(IConnection connection, ResiliencePipelineProvider<string> pipelineProvider)
{
    public async Task PublishAsync(ImageJobMessage message)
    {
        var pipeline = pipelineProvider.GetPipeline("messaging");
        await pipeline.ExecuteAsync(async ct =>
        {
            await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

            await channel.BasicPublishAsync(
                exchange: "",
                routingKey: QueueNames.ImageJobs,
                mandatory: false,
                basicProperties: new BasicProperties { Persistent = true },
                body: body,
                cancellationToken: ct
            );
        });
    }
}