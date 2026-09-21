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

    public int? MessageTtlMilliseconds { get; set; }

    public RabbitMqBindingOptions Measurements { get; set; } = new()
    {
        RoutingKey = "eda.sync.result",
        QueueName = "egwn.measurements",
        DeadLetterQueue = "egwn.measurements.dlq"
    };

    public RabbitMqBindingOptions Kpis { get; set; } = new()
    {
        RoutingKey = "eda.sync.kpi",
        QueueName = "egwn.kpis",
        DeadLetterQueue = "egwn.kpis.dlq"
    };
}

public sealed class RabbitMqBindingOptions
{
    public string RoutingKey { get; set; } = string.Empty;

    public string QueueName { get; set; } = string.Empty;

    public string DeadLetterQueue { get; set; } = string.Empty;
}
