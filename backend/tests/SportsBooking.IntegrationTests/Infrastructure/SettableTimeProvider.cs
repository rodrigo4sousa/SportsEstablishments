namespace SportsBooking.IntegrationTests.Infrastructure;

/// <summary>
/// A clock a test can put at any moment, backwards too (<c>FakeTimeProvider.SetUtcNow</c> refuses to go back). Sessions must be
/// made while the host's clock is at the real time, because an access token is checked against the system clock; the test then
/// moves it to the date it is about.
/// </summary>
public sealed class SettableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private long _utcTicks = start.UtcTicks;

    public void SetUtcNow(DateTimeOffset value) => Interlocked.Exchange(ref _utcTicks, value.UtcTicks);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _utcTicks, by.Ticks);

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
}
