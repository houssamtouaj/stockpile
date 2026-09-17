namespace Stockpile.Application.Common.Interfaces;

/// <summary>
/// The only sanctioned source of time. An architecture test (task 13) fails the build if
/// any type outside the implementation reads DateTime.Now or DateTimeOffset.UtcNow —
/// otherwise the 90-day backdated seed data and the slow-movers report cannot be tested.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
