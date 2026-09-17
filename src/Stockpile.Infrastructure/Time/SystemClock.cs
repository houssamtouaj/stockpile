using Stockpile.Application.Common.Interfaces;

namespace Stockpile.Infrastructure.Time;

/// <summary>The one place in the codebase permitted to read the wall clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
