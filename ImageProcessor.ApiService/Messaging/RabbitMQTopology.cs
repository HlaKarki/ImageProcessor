using ImageProcessor.Contracts.Messages;
using RabbitMQ.Client;

namespace ImageProcessor.ApiService.Messaging;

public static class RabbitMQTopology
{
    public static async Task ConfigureAsync(IConnection connection)
    {
        await using var channel = await connection.CreateChannelAsync();

        await channel.QueueDeclareAsync(
            queue: QueueNames.ImageJobs,
            durable: true,
            exclusive: false,
            autoDelete: false
        );
    }
}