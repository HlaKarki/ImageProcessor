using System.Text;
using System.Text.Json;
using ImageProcessor.Contracts.Messages;
using RabbitMQ.Client;

namespace ImageProcessor.ApiService.Services;

public class MessagePublisher(IConnection connection)
{
    public async Task PublishAsync(ImageJobMessage message)
    {
        await using var channel = await connection.CreateChannelAsync();
        
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        
        await channel.BasicPublishAsync(
            exchange: "",
            routingKey: QueueNames.ImageJobs,
            mandatory: false,
            basicProperties: new BasicProperties { Persistent = true },
            body: body
        );
    }
}