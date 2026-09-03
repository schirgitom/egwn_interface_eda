namespace EGWNInterfaceEda.Application.Options;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";

    public int Port { get; set; } = 5672;

    public string UserName { get; set; } = "guest";

    public string Password { get; set; } = "guest";

    public string VirtualHost { get; set; } = "/";

    public string Exchange { get; set; } = "eda.sync";

    public string ExchangeType { get; set; } = "topic";

    public string RoutingKey { get; set; } = "eda.sync.result";

    public string QueueName { get; set; } = "egwn.measurements";

    public int? MessageTtlMilliseconds { get; set; }
}
