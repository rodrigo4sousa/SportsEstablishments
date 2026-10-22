using System.Globalization;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.UnitTests.Scheduling;

public sealed class SlotGeneratorTests
{
    private static readonly TimeZoneInfo Lisbon = ClubTimeZones.Lisbon;

    // Daylight saving changes in Lisbon in 2026.
    private static readonly DateOnly SpringForward = new(2026, 3, 29); // 01:00 WET -> 02:00 WEST
    private static readonly DateOnly FallBack = new(2026, 10, 25); // 02:00 WEST -> 01:00 WET

    private static OpeningHours Hours(string opens, string closes) =>
        OpeningHours.Create(TimeOnly.Parse(opens, CultureInfo.InvariantCulture), TimeOnly.Parse(closes, CultureInfo.InvariantCulture));

    private static IReadOnlyList<TimeSlot> Generate(DateOnly date, string opens, string closes, int minutes) =>
        SlotGenerator.Generate(date, Hours(opens, closes), SlotDuration.FromMinutes(minutes), Lisbon);

    private static string[] LocalTimes(IEnumerable<TimeSlot> slots) =>
        slots.Select(slot =>
                $"{ClubTime.ToLocal(slot.StartsAtUtc, Lisbon):HH:mm}-{ClubTime.ToLocal(slot.EndsAtUtc, Lisbon):HH:mm}")
            .ToArray();

    private static string[] UtcTimes(IEnumerable<TimeSlot> slots) =>
        slots.Select(slot => $"{slot.StartsAtUtc:dd HH:mm}-{slot.EndsAtUtc:dd HH:mm}").ToArray();

    private static void ShouldBeContiguousUtcSlotsOf(IReadOnlyList<TimeSlot> slots, int minutes)
    {
        foreach (var slot in slots)
        {
            slot.StartsAtUtc.Kind.ShouldBe(DateTimeKind.Utc);
            slot.EndsAtUtc.Kind.ShouldBe(DateTimeKind.Utc);
            (slot.EndsAtUtc - slot.StartsAtUtc).ShouldBe(TimeSpan.FromMinutes(minutes));
        }

        for (var i = 1; i < slots.Count; i++)
        {
            slots[i].StartsAtUtc.ShouldBe(slots[i - 1].EndsAtUtc);
        }
    }

    [Fact]
    public void Summer_day_slots_are_stored_one_hour_behind_lisbon_time()
    {
        var slots = Generate(new DateOnly(2026, 7, 15), "09:00", "12:00", 60);

        LocalTimes(slots).ShouldBe(["09:00-10:00", "10:00-11:00", "11:00-12:00"]);
        UtcTimes(slots).ShouldBe(["15 08:00-15 09:00", "15 09:00-15 10:00", "15 10:00-15 11:00"]);
        ShouldBeContiguousUtcSlotsOf(slots, 60);
    }

    [Fact]
    public void Winter_day_slots_match_utc()
    {
        var slots = Generate(new DateOnly(2026, 1, 15), "09:00", "11:00", 60);

        UtcTimes(slots).ShouldBe(["15 09:00-15 10:00", "15 10:00-15 11:00"]);
    }

    [Theory]
    [InlineData(30, 6)]
    [InlineData(60, 3)]
    [InlineData(90, 2)]
    public void Pitches_with_different_slot_durations_split_the_same_hours_differently(int minutes, int expectedCount)
    {
        var slots = Generate(new DateOnly(2026, 5, 4), "09:00", "12:00", minutes);

        slots.Count.ShouldBe(expectedCount);
        ClubTime.ToLocal(slots[0].StartsAtUtc, Lisbon).TimeOfDay.ShouldBe(new TimeSpan(9, 0, 0));
        ClubTime.ToLocal(slots[^1].EndsAtUtc, Lisbon).TimeOfDay.ShouldBe(new TimeSpan(12, 0, 0));
        ShouldBeContiguousUtcSlotsOf(slots, minutes);
    }

    [Fact]
    public void Block_that_would_end_after_closing_is_not_generated()
    {
        // 09:00-11:00 with 90-minute slots: 09:00-10:30 fits, 10:30-12:00 would pass closing.
        LocalTimes(Generate(new DateOnly(2026, 5, 4), "09:00", "11:00", 90)).ShouldBe(["09:00-10:30"]);
        // 09:00-10:00 with 90-minute slots: nothing fits.
        Generate(new DateOnly(2026, 5, 4), "09:00", "10:00", 90).ShouldBeEmpty();
        // Opening not on the hour.
        LocalTimes(Generate(new DateOnly(2026, 5, 4), "09:15", "10:45", 30))
            .ShouldBe(["09:15-09:45", "09:45-10:15", "10:15-10:45"]);
    }

    [Fact]
    public void Closing_at_midnight_includes_the_last_slot_ending_at_midnight()
    {
        var slots = Generate(new DateOnly(2026, 7, 31), "22:00", "00:00", 60);

        LocalTimes(slots).ShouldBe(["22:00-23:00", "23:00-00:00"]);
        // 23:00-00:00 WEST on 31 July is 22:00-23:00 UTC on the same day.
        UtcTimes(slots).ShouldBe(["31 21:00-31 22:00", "31 22:00-31 23:00"]);
    }

    [Fact]
    public void Slot_that_would_cross_midnight_is_not_generated()
    {
        LocalTimes(Generate(new DateOnly(2026, 7, 31), "22:30", "00:00", 60)).ShouldBe(["22:30-23:30"]);
        Generate(new DateOnly(2026, 7, 31), "23:00", "00:00", 90).ShouldBeEmpty();
    }

    [Fact]
    public void Day_open_from_midnight_to_midnight_has_24_hourly_slots_on_a_normal_day()
    {
        var slots = Generate(new DateOnly(2026, 6, 10), "00:00", "00:00", 60);

        slots.Count.ShouldBe(24);
        slots[0].StartsAtUtc.ShouldBe(new DateTime(2026, 6, 9, 23, 0, 0, DateTimeKind.Utc));
        slots[^1].EndsAtUtc.ShouldBe(new DateTime(2026, 6, 10, 23, 0, 0, DateTimeKind.Utc));
        ShouldBeContiguousUtcSlotsOf(slots, 60);
    }

    [Fact]
    public void Spring_forward_day_with_normal_hours_uses_summer_time()
    {
        var slots = Generate(SpringForward, "08:00", "23:00", 60);

        slots.Count.ShouldBe(15);
        slots[0].StartsAtUtc.ShouldBe(new DateTime(2026, 3, 29, 7, 0, 0, DateTimeKind.Utc));
        slots[^1].EndsAtUtc.ShouldBe(new DateTime(2026, 3, 29, 22, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Spring_forward_day_loses_the_missing_hour()
    {
        // 00:00-04:00 local is only 3 real hours because 01:00-02:00 does not exist.
        var slots = Generate(SpringForward, "00:00", "04:00", 60);

        UtcTimes(slots).ShouldBe(["29 00:00-29 01:00", "29 01:00-29 02:00", "29 02:00-29 03:00"]);
        // The first slot starts at 00:00 WET and ends at 02:00 WEST: still 60 minutes.
        LocalTimes(slots).ShouldBe(["00:00-02:00", "02:00-03:00", "03:00-04:00"]);
        ShouldBeContiguousUtcSlotsOf(slots, 60);
    }

    [Fact]
    public void Spring_forward_day_open_all_day_has_23_hourly_slots()
    {
        var slots = Generate(SpringForward, "00:00", "00:00", 60);

        slots.Count.ShouldBe(23);
        ShouldBeContiguousUtcSlotsOf(slots, 60);
    }

    [Fact]
    public void Spring_forward_day_with_30_minute_slots_skips_the_missing_hour()
    {
        var slots = Generate(SpringForward, "00:00", "03:00", 30);

        // 2 real hours: 00:00-01:00 WET, then 02:00-03:00 WEST.
        LocalTimes(slots).ShouldBe(["00:00-00:30", "00:30-02:00", "02:00-02:30", "02:30-03:00"]);
        ShouldBeContiguousUtcSlotsOf(slots, 30);
    }

    [Fact]
    public void Opening_time_inside_the_spring_forward_gap_starts_after_the_jump()
    {
        // 01:30 does not exist on 29 March; it is read as 02:30 WEST.
        var slots = Generate(SpringForward, "01:30", "04:00", 30);

        LocalTimes(slots).ShouldBe(["02:30-03:00", "03:00-03:30", "03:30-04:00"]);
    }

    [Fact]
    public void Fall_back_day_with_normal_hours_uses_winter_time()
    {
        var slots = Generate(FallBack, "08:00", "23:00", 60);

        slots.Count.ShouldBe(15);
        slots[0].StartsAtUtc.ShouldBe(new DateTime(2026, 10, 25, 8, 0, 0, DateTimeKind.Utc));
        slots[^1].EndsAtUtc.ShouldBe(new DateTime(2026, 10, 25, 23, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Fall_back_day_gains_the_repeated_hour()
    {
        // 00:00-04:00 local is 5 real hours because 01:00-02:00 happens twice.
        var slots = Generate(FallBack, "00:00", "04:00", 60);

        UtcTimes(slots).ShouldBe(
        [
            "24 23:00-25 00:00",
            "25 00:00-25 01:00",
            "25 01:00-25 02:00",
            "25 02:00-25 03:00",
            "25 03:00-25 04:00",
        ]);
        // The second slot runs from 01:00 WEST to 01:00 WET (the moment clocks go back), so its wall-clock
        // start and end are equal; the third covers the repeated 01:00-02:00 in WET. All are different instants.
        LocalTimes(slots).ShouldBe(["00:00-01:00", "01:00-01:00", "01:00-02:00", "02:00-03:00", "03:00-04:00"]);
        ShouldBeContiguousUtcSlotsOf(slots, 60);
    }

    [Fact]
    public void Fall_back_day_open_all_day_has_25_hourly_slots()
    {
        var slots = Generate(FallBack, "00:00", "00:00", 60);

        slots.Count.ShouldBe(25);
        ShouldBeContiguousUtcSlotsOf(slots, 60);
    }

    [Fact]
    public void Fall_back_day_with_90_minute_slots_drops_the_block_past_closing()
    {
        // 00:00 WEST (23:00 UTC) to 03:00 WET (03:00 UTC) is 4 real hours: two 90-minute slots fit, a third would not.
        var slots = Generate(FallBack, "00:00", "03:00", 90);

        UtcTimes(slots).ShouldBe(["24 23:00-25 00:30", "25 00:30-25 02:00"]);
        ShouldBeContiguousUtcSlotsOf(slots, 90);
    }

    [Fact]
    public void Closing_time_in_the_repeated_hour_uses_its_first_occurrence()
    {
        // Closing at 01:30 on 25 October is the first 01:30 (WEST, 00:30 UTC).
        var slots = Generate(FallBack, "00:00", "01:30", 30);

        LocalTimes(slots).ShouldBe(["00:00-00:30", "00:30-01:00", "01:00-01:30"]);
        slots[^1].EndsAtUtc.ShouldBe(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Days_around_the_changes_use_their_own_offsets()
    {
        Generate(SpringForward.AddDays(-1), "10:00", "11:00", 60)[0].StartsAtUtc.Hour.ShouldBe(10);
        Generate(SpringForward.AddDays(1), "10:00", "11:00", 60)[0].StartsAtUtc.Hour.ShouldBe(9);
        Generate(FallBack.AddDays(-1), "10:00", "11:00", 60)[0].StartsAtUtc.Hour.ShouldBe(9);
        Generate(FallBack.AddDays(1), "10:00", "11:00", 60)[0].StartsAtUtc.Hour.ShouldBe(10);
    }

    [Fact]
    public void Default_slot_duration_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            SlotGenerator.Generate(new DateOnly(2026, 5, 4), Hours("09:00", "10:00"), default, Lisbon));
    }
}
