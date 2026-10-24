using SportsBooking.Domain.Scheduling;

namespace SportsBooking.UnitTests.Scheduling;

public sealed class TimeSlotTests
{
    private static readonly TimeSlot Slot = new(
        new DateTime(2026, 5, 4, 8, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 5, 4, 9, 0, 0, DateTimeKind.Utc));

    [Theory]
    [InlineData("2026-05-04T07:59:59Z", false)] // a second before the start
    [InlineData("2026-05-04T08:00:00Z", true)] // starting right now counts as started
    [InlineData("2026-05-04T08:30:00Z", true)] // under way
    [InlineData("2026-05-04T09:00:00Z", true)] // over
    public void A_slot_has_started_from_its_start_instant(string now, bool expected)
    {
        var instant = DateTime.Parse(now, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);

        Slot.HasStartedBy(instant).ShouldBe(expected);
    }
}
