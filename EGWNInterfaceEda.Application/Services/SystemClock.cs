using EGWNInterfaceEda.Application.Abstractions;

namespace EGWNInterfaceEda.Application.Services;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
