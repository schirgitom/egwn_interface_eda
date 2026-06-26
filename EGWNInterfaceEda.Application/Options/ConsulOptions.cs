namespace EGWNInterfaceEda.Application.Options;

public sealed class ConsulOptions
{
    public const string SectionName = "Consul";

    public string? Address { get; set; }

    public string ServiceName { get; set; } = "egwn-interface-eda";

    public string ServiceId { get; set; } = $"{Environment.MachineName}-egwn-interface-eda";

    public string[] Tags { get; set; } = [];

    public bool Enabled { get; set; } = true;

    public TimeSpan Ttl { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(10);
}
