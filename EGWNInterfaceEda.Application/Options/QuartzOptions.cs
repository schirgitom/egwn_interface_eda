namespace EGWNInterfaceEda.Application.Options;

public sealed class QuartzOptions
{
    public const string SectionName = "Quartz";

    public int IntervalMinutes { get; set; } = 180;
}
