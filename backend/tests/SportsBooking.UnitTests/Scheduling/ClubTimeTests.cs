using Microsoft.Extensions.Time.Testing;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.UnitTests.Scheduling;

public sealed class ClubTimeTests
{
    private static readonly TimeZoneInfo Lisbon = ClubTimeZones.Lisbon;

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void Lisbon_time_zone_resolves_from_its_iana_id()
    {
        ClubTimeZones.Lisbon.Id.ShouldBe(ClubTimeZones.LisbonId);
    }

    [Theory]
    [InlineData(2026, 1, 15, 10, 0, 10)] // winter, WET (UTC+0)
    [InlineData(2026, 7, 15, 10, 0, 9)] // summer, WEST (UTC+1)
    [InlineData(2026, 3, 28, 23, 0, 23)] // the day before clocks go forward is still UTC+0
    [InlineData(2026, 3, 29, 3, 0, 2)] // after the jump on 29 March: UTC+1
    [InlineData(2026, 10, 25, 3, 0, 3)] // after clocks go back on 25 October: UTC+0
    public void Converts_unambiguous_local_times_to_utc(int year, int month, int day, int hour, int minute, int expectedUtcHour)
    {
        var utc = ClubTime.ToUtc(new DateOnly(year, month, day), new TimeOnly(hour, minute), Lisbon);

        utc.ShouldBe(Utc(year, month, day, expectedUtcHour, minute));
        utc.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void Nonexistent_local_time_on_spring_forward_day_is_shifted_past_the_gap()
    {
        // 29 March 2026: at 01:00 WET clocks jump to 02:00 WEST, so 01:30 never happens.
        var utc = ClubTime.ToUtc(new DateOnly(2026, 3, 29), new TimeOnly(1, 30), Lisbon);

        utc.ShouldBe(Utc(2026, 3, 29, 1, 30));
        ClubTime.ToLocal(utc, Lisbon).ShouldBe(new DateTime(2026, 3, 29, 2, 30, 0));
    }

    [Fact]
    public void Repeated_local_time_on_fall_back_day_resolves_to_first_occurrence()
    {
        // 25 October 2026: at 02:00 WEST clocks go back to 01:00 WET, so 01:30 happens twice.
        var utc = ClubTime.ToUtc(new DateOnly(2026, 10, 25), new TimeOnly(1, 30), Lisbon);

        utc.ShouldBe(Utc(2026, 10, 25, 0, 30));
    }

    [Fact]
    public void Local_midnight_in_summer_is_the_previous_day_in_utc()
    {
        var utc = ClubTime.ToUtc(new DateOnly(2026, 8, 1), TimeOnly.MinValue, Lisbon);

        utc.ShouldBe(Utc(2026, 7, 31, 23));
    }

    [Fact]
    public void ToLocal_rejects_non_utc_instants()
    {
        Should.Throw<ArgumentException>(() => ClubTime.ToLocal(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Local), Lisbon));
        Should.Throw<ArgumentException>(() => ClubTime.ToLocal(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Unspecified), Lisbon));
    }

    [Theory]
    [InlineData("2026-07-31T22:59:59Z", "2026-07-31")] // 23:59:59 WEST
    [InlineData("2026-07-31T23:00:00Z", "2026-08-01")] // already 00:00 WEST the next day
    [InlineData("2026-12-31T23:59:59Z", "2026-12-31")] // winter: Lisbon date equals UTC date
    [InlineData("2027-01-01T00:00:00Z", "2027-01-01")]
    [InlineData("2026-03-28T23:30:00Z", "2026-03-28")] // night of the spring-forward change
    [InlineData("2026-10-24T23:30:00Z", "2026-10-25")] // night of the fall-back change, 00:30 WEST
    public void Today_uses_the_lisbon_calendar_date(string utcNow, string expectedDate)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse(utcNow, System.Globalization.CultureInfo.InvariantCulture));

        ClubTime.Today(clock, Lisbon).ShouldBe(DateOnly.Parse(expectedDate, System.Globalization.CultureInfo.InvariantCulture));
    }
}
