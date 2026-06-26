using System.Text;
using System.Text.Json;
using EGWNInterfaceEda.Domain;
using EGWNInterfaceEda.Application.Abstractions;
using EGWNInterfaceEda.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace EGWNInterfaceEda.Infrastructure.Services;

public sealed class RabbitMqEdaResultPublisher : IEdaResultPublisher, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqEdaResultPublisher> _logger;
    private readonly object _sync = new();
    private IConnection? _connection;
    private IModel? _channel;

    public RabbitMqEdaResultPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqEdaResultPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task PublishAsync(EdaSyncPublication publication, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var channel = EnsureChannel();
        var payload = JsonSerializer.Serialize(publication, SerializerOptions);
        var body = Encoding.UTF8.GetBytes(payload);

        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";
        properties.MessageId = Guid.NewGuid().ToString("N");
        properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        channel.BasicPublish(_options.Exchange, _options.RoutingKey, properties, body);
        return Task.CompletedTask;
    }

    private IModel EnsureChannel()
    {
        lock (_sync)
        {
            if (_channel is { IsOpen: true })
            {
                return _channel;
            }

            _connection?.Dispose();

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                DispatchConsumersAsync = true
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            _channel.ExchangeDeclare(_options.Exchange, _options.ExchangeType, durable: true, autoDelete: false, arguments: null);
            _logger.LogInformation("RabbitMQ publisher connected to {Host}:{Port}", _options.HostName, _options.Port);
            return _channel;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _channel?.Dispose();
            _connection?.Dispose();
            _channel = null;
            _connection = null;
        }
    }
}
