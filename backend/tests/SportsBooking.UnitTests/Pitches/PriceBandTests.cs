using System.Globalization;
using SportsBooking.Domain.Pitches;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.UnitTests.Pitches;

/// <summary>US10: the price of a slot comes from the band that contains its local start, and bands never overlap.</summary>
public sealed class PriceBandTests
{
    private static readonly TimeZoneInfo Lisbon = ClubTimeZones.Lisbon;
    private static readonly DateTime Now = new(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc);

    private static readonly DayOfWeek[] Weekdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    private static Pitch NewPitch(decimal price = 20m) =>
        Pitch.Create(Guid.CreateVersion7(), new PitchDetails("Campo 1", Sport.Padel, price), Now);

    private static PriceBandDetails Band(string starts, string ends, decimal price, params DayOfWeek[] days) =>
        new(days.ToHashSet(), TimeOnly.Parse(starts, CultureInfo.InvariantCulture), TimeOnly.Parse(ends, CultureInfo.InvariantCulture), price);

    private static Pitch WithBand(string starts, string ends, decimal price, params DayOfWeek[] days)
    {
        var pitch = NewPitch();
        pitch.TryAddPriceBand(Band(starts, ends, price, days), Now, out _).ShouldBe(PriceBandOutcome.Done);
        return pitch;
    }

    /// <summary>The price of the slot starting at this Lisbon wall-clock time.</summary>
    private static decimal PriceAt(Pitch pitch, string date, string time) =>
        pitch.PriceForSlot(ClubTime.ToUtc(DateOnly.Parse(date, CultureInfo.InvariantCulture), TimeOnly.Parse(time, CultureInfo.InvariantCulture), Lisbon), Lisbon);

    // 2026-10-26 is a Monday, 10-24 a Saturday, 10-25 a Sunday.
    private const string Monday = "2026-10-26";
    private const string Saturday = "2026-10-24";

    // ---- The price function

    [Fact]
    public void A_pitch_without_bands_charges_its_base_price_at_any_time()
    {
        var pitch = NewPitch(18.5m);

        PriceAt(pitch, Monday, "09:00").ShouldBe(18.5m);
        PriceAt(pitch, Saturday, "23:30").ShouldBe(18.5m);
        pitch.HasPriceBands.ShouldBeFalse();
    }

    [Fact]
    public void A_slot_inside_a_band_costs_the_band_price_and_one_outside_costs_the_base_price()
    {
        var pitch = WithBand("18:00", "22:00", 30m, Weekdays);

        PriceAt(pitch, Monday, "19:00").ShouldBe(30m);
        PriceAt(pitch, Monday, "17:00").ShouldBe(20m);
        PriceAt(pitch, Monday, "09:00").ShouldBe(20m);
        pitch.HasPriceBands.ShouldBeTrue();
    }

    [Fact]
    public void The_band_start_is_included_and_its_end_is_not()
    {
        var pitch = WithBand("18:00", "22:00", 30m, DayOfWeek.Monday);

        PriceAt(pitch, Monday, "17:30").ShouldBe(20m);
        PriceAt(pitch, Monday, "18:00").ShouldBe(30m);
        PriceAt(pitch, Monday, "21:30").ShouldBe(30m);
        PriceAt(pitch, Monday, "22:00").ShouldBe(20m); // a slot starting at the band end is outside it
    }

    [Fact]
    public void A_slot_that_runs_past_the_end_of_a_band_takes_the_price_of_the_band_where_it_starts()
    {
        var pitch = WithBand("18:00", "22:00", 30m, DayOfWeek.Monday);

        // A 90-minute slot from 21:00 ends at 22:30: it starts inside the band, so it is 30.
        PriceAt(pitch, Monday, "21:00").ShouldBe(30m);
        // And one from 16:30 to 18:00 starts outside it, so it is the base price.
        PriceAt(pitch, Monday, "16:30").ShouldBe(20m);
    }

    [Fact]
    public void A_band_that_ends_at_midnight_covers_the_last_slots_of_the_day()
    {
        var pitch = WithBand("22:00", "00:00", 35m, DayOfWeek.Monday);

        PriceAt(pitch, Monday, "21:30").ShouldBe(20m);
        PriceAt(pitch, Monday, "22:00").ShouldBe(35m);
        PriceAt(pitch, Monday, "23:30").ShouldBe(35m);
        // The next day's 00:00 slot is Tuesday's, not Monday's.
        PriceAt(pitch, "2026-10-27", "00:00").ShouldBe(20m);
    }

    [Fact]
    public void A_band_from_midnight_to_midnight_covers_the_whole_day()
    {
        var pitch = WithBand("00:00", "00:00", 25m, DayOfWeek.Monday);

        PriceAt(pitch, Monday, "00:00").ShouldBe(25m);
        PriceAt(pitch, Monday, "23:30").ShouldBe(25m);
    }

    [Fact]
    public void A_sunday_band_does_not_apply_on_saturday()
    {
        var pitch = WithBand("10:00", "14:00", 40m, DayOfWeek.Sunday);

        PriceAt(pitch, Saturday, "11:00").ShouldBe(20m);
        PriceAt(pitch, "2026-10-25", "11:00").ShouldBe(40m);
        PriceAt(pitch, Monday, "11:00").ShouldBe(20m);
    }

    [Fact]
    public void Bands_on_different_weekdays_or_times_each_price_their_own_slots()
    {
        var pitch = NewPitch();
        pitch.TryAddPriceBand(Band("18:00", "22:00", 30m, Weekdays), Now, out _);
        pitch.TryAddPriceBand(Band("09:00", "13:00", 24m, DayOfWeek.Saturday, DayOfWeek.Sunday), Now, out _);

        PriceAt(pitch, Monday, "19:00").ShouldBe(30m);
        PriceAt(pitch, Saturday, "10:00").ShouldBe(24m);
        PriceAt(pitch, Saturday, "19:00").ShouldBe(20m);
    }

    [Fact]
    public void The_two_slots_at_one_oclock_on_the_day_the_clocks_go_back_are_priced_by_local_time()
    {
        // 2026-10-25: 02:00 WEST becomes 01:00 WET, so local 01:00 happens twice (00:00 UTC and 01:00 UTC).
        var pitch = WithBand("01:00", "03:00", 33m, DayOfWeek.Sunday);
        var summerOne = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
        var winterOne = new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc);
        var beforeBand = new DateTime(2026, 10, 24, 23, 30, 0, DateTimeKind.Utc); // 00:30 WEST

        ClubTime.ToLocal(summerOne, Lisbon).Hour.ShouldBe(1);
        ClubTime.ToLocal(winterOne, Lisbon).Hour.ShouldBe(1);
        pitch.PriceForSlot(summerOne, Lisbon).ShouldBe(33m);
        pitch.PriceForSlot(winterOne, Lisbon).ShouldBe(33m);
        pitch.PriceForSlot(winterOne.AddHours(1), Lisbon).ShouldBe(33m); // 02:00 WET
        pitch.PriceForSlot(winterOne.AddHours(2), Lisbon).ShouldBe(20m); // 03:00 WET, the band end
        pitch.PriceForSlot(beforeBand, Lisbon).ShouldBe(20m);
    }

    // ---- Overlap

    [Fact]
    public void Bands_that_only_touch_are_accepted()
    {
        var pitch = WithBand("18:00", "20:00", 30m, DayOfWeek.Monday);

        pitch.TryAddPriceBand(Band("20:00", "22:00", 35m, DayOfWeek.Monday), Now, out var next).ShouldBe(PriceBandOutcome.Done);
        pitch.TryAddPriceBand(Band("16:00", "18:00", 25m, DayOfWeek.Monday), Now, out _).ShouldBe(PriceBandOutcome.Done);

        next.ShouldNotBeNull();
        pitch.PriceBands.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData("19:00", "21:00")]
    [InlineData("18:00", "20:00")]
    [InlineData("17:00", "23:00")]
    [InlineData("18:30", "19:00")]
    public void Bands_that_share_a_weekday_and_intersect_in_time_are_refused(string starts, string ends)
    {
        var pitch = WithBand("18:00", "20:00", 30m, DayOfWeek.Monday);

        pitch.TryAddPriceBand(Band(starts, ends, 35m, DayOfWeek.Monday, DayOfWeek.Tuesday), Now, out var band).ShouldBe(PriceBandOutcome.Overlaps);

        band.ShouldBeNull();
        pitch.PriceBands.Count.ShouldBe(1);
    }

    [Fact]
    public void The_same_hours_on_other_weekdays_do_not_overlap()
    {
        var pitch = WithBand("18:00", "20:00", 30m, DayOfWeek.Monday);

        pitch.TryAddPriceBand(Band("18:00", "20:00", 35m, DayOfWeek.Tuesday, DayOfWeek.Sunday), Now, out _).ShouldBe(PriceBandOutcome.Done);
    }

    [Fact]
    public void A_band_to_midnight_overlaps_a_late_band_and_touches_an_earlier_one()
    {
        var pitch = WithBand("22:00", "00:00", 35m, DayOfWeek.Monday);

        pitch.TryAddPriceBand(Band("23:00", "23:30", 40m, DayOfWeek.Monday), Now, out _).ShouldBe(PriceBandOutcome.Overlaps);
        pitch.TryAddPriceBand(Band("20:00", "22:00", 30m, DayOfWeek.Monday), Now, out _).ShouldBe(PriceBandOutcome.Done);
        pitch.TryAddPriceBand(Band("00:00", "00:00", 10m, DayOfWeek.Monday), Now, out _).ShouldBe(PriceBandOutcome.Overlaps);
    }

    [Fact]
    public void Changing_a_band_does_not_make_it_overlap_itself_but_does_overlap_the_others()
    {
        var pitch = NewPitch();
        pitch.TryAddPriceBand(Band("18:00", "20:00", 30m, DayOfWeek.Monday), Now, out var first);
        pitch.TryAddPriceBand(Band("20:00", "22:00", 35m, DayOfWeek.Monday), Now, out _);

        pitch.TryChangePriceBand(first!, Band("17:00", "20:00", 31m, DayOfWeek.Monday), Now.AddHours(1)).ShouldBe(PriceBandOutcome.Done);
        pitch.TryChangePriceBand(first!, Band("17:00", "21:00", 31m, DayOfWeek.Monday), Now.AddHours(1)).ShouldBe(PriceBandOutcome.Overlaps);

        first!.Starts.ShouldBe(new TimeOnly(17, 0));
        first.Ends.ShouldBe(new TimeOnly(20, 0));
        first.Price.ShouldBe(31m);
        first.UpdatedAt.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public void A_pitch_takes_at_most_the_maximum_number_of_bands()
    {
        var pitch = NewPitch();
        for (var slot = 0; slot < PriceBandRules.MaximumPerPitch; slot++)
        {
            // Distinct 30-minute ranges on Monday: 00:00-00:30, 00:30-01:00, …
            var start = TimeSpan.FromMinutes(slot * 30);
            var end = start + TimeSpan.FromMinutes(30);
            pitch.TryAddPriceBand(new PriceBandDetails(new HashSet<DayOfWeek> { DayOfWeek.Monday }, TimeOnly.FromTimeSpan(start), TimeOnly.FromTimeSpan(end), 20m), Now, out _)
                .ShouldBe(PriceBandOutcome.Done);
        }

        pitch.TryAddPriceBand(Band("15:00", "16:00", 20m, DayOfWeek.Tuesday), Now, out var band).ShouldBe(PriceBandOutcome.LimitReached);

        band.ShouldBeNull();
        pitch.PriceBands.Count.ShouldBe(PriceBandRules.MaximumPerPitch);
    }

    [Fact]
    public void A_removed_band_no_longer_prices_anything()
    {
        var pitch = NewPitch();
        pitch.TryAddPriceBand(Band("18:00", "22:00", 30m, DayOfWeek.Monday), Now, out var band);

        pitch.RemovePriceBand(band!);

        PriceAt(pitch, Monday, "19:00").ShouldBe(20m);
        pitch.HasPriceBands.ShouldBeFalse();
    }

    // ---- The band itself

    [Fact]
    public void A_band_has_a_v7_id_and_lists_its_weekdays()
    {
        var pitch = WithBand("18:00", "22:00", 30m, DayOfWeek.Saturday, DayOfWeek.Monday);

        var band = pitch.PriceBands.ShouldHaveSingleItem();

        band.Id.Version.ShouldBe(7);
        band.PitchId.ShouldBe(pitch.Id);
        band.Days.ShouldBe([DayOfWeek.Monday, DayOfWeek.Saturday], ignoreOrder: true);
        band.CreatedAt.ShouldBe(Now);
    }

    [Fact]
    public void A_band_the_entity_cannot_hold_is_a_programming_error()
    {
        var pitch = NewPitch();

        Should.Throw<ArgumentException>(() => pitch.TryAddPriceBand(Band("18:00", "22:00", 30m), Now, out _)); // no weekday
        Should.Throw<ArgumentException>(() => pitch.TryAddPriceBand(Band("18:15", "22:00", 30m, DayOfWeek.Monday), Now, out _)); // off the step
        Should.Throw<ArgumentException>(() => pitch.TryAddPriceBand(Band("22:00", "18:00", 30m, DayOfWeek.Monday), Now, out _)); // end before start
        Should.Throw<ArgumentOutOfRangeException>(() => pitch.TryAddPriceBand(Band("18:00", "22:00", 0m, DayOfWeek.Monday), Now, out _));
        Should.Throw<ArgumentOutOfRangeException>(() => pitch.TryAddPriceBand(Band("18:00", "22:00", 500.01m, DayOfWeek.Monday), Now, out _));
    }
}
