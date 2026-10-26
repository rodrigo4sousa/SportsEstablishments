using SportsBooking.Application.Statistics;
using SportsBooking.Domain.Pitches;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.UnitTests.Statistics;

public sealed class OccupationTests
{
    private static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById(ClubTimeZones.LisbonId);

    private static readonly DateOnly Today = new(2026, 5, 13);

    private static IReadOnlyList<PitchOpeningDay> Schedule(params (DayOfWeek Day, string Opens, string Closes)[] open) => Schedule(60, open);

    private static IReadOnlyList<PitchOpeningDay> Schedule(int slotMinutes, params (DayOfWeek Day, string Opens, string Closes)[] open)
    {
        var pitch = Pitch.Create(Guid.CreateVersion7(), new PitchDetails("Campo", Sport.Padel, 20m), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var week = Enum.GetValues<DayOfWeek>().ToDictionary(day => day, _ => (OpeningHours?)null);
        foreach (var (day, opens, closes) in open)
        {
            week[day] = OpeningHours.Create(TimeOnly.Parse(opens, System.Globalization.CultureInfo.InvariantCulture), TimeOnly.Parse(closes, System.Globalization.CultureInfo.InvariantCulture));
        }

        pitch.ChangeSchedule(SlotDuration.FromMinutes(slotMinutes), week, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        return pitch.OpeningDays;
    }

    private static StatisticsPeriod Period(string from, string to) => new(DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture));

    // ---- Open hours

    [Theory]
    [InlineData("2026-10-25", 25 * 60)]
    [InlineData("2026-03-29", 23 * 60)]
    [InlineData("2026-10-24", 24 * 60)]
    public void A_pitch_open_all_day_is_open_for_the_real_length_of_the_day(string date, int minutes)
    {
        var allDay = Schedule(Enum.GetValues<DayOfWeek>().Select(day => (day, "00:00", "00:00")).ToArray());

        OccupationService.OpenMinutes(allDay, SlotDuration.SixtyMinutes, Period(date, date), Lisbon).ShouldBe(minutes);
    }

    [Fact]
    public void Closed_days_count_nothing_and_each_open_day_counts_its_interval()
    {
        var mondays = Schedule((DayOfWeek.Monday, "09:00", "13:00"), (DayOfWeek.Wednesday, "18:00", "00:00"));

        // 4 to 17 May 2026: two Mondays (4 h each) and two Wednesdays (6 h each, to midnight).
        OccupationService.OpenMinutes(mondays, SlotDuration.SixtyMinutes, Period("2026-05-04", "2026-05-17"), Lisbon).ShouldBe(2 * 240 + 2 * 360);
        OccupationService.OpenMinutes(mondays, SlotDuration.SixtyMinutes, Period("2026-05-05", "2026-05-05"), Lisbon).ShouldBe(0);
    }

    [Theory]
    [InlineData(30, "09:00", "13:00", 240)] // 8 slots
    [InlineData(60, "09:00", "13:00", 240)] // 4 slots
    [InlineData(90, "09:00", "13:00", 180)] // 2 slots of 90 minutes; the last 60 minutes cannot be booked
    [InlineData(90, "09:00", "12:00", 180)]
    [InlineData(90, "09:00", "10:00", 0)] // not even one slot fits
    [InlineData(60, "09:30", "12:00", 120)]
    public void Open_time_is_the_bookable_slots_not_the_whole_interval(int slotMinutes, string opens, string closes, int minutes)
    {
        var monday = Schedule(slotMinutes, (DayOfWeek.Monday, opens, closes));

        OccupationService.OpenMinutes(monday, SlotDuration.FromMinutes(slotMinutes), Period("2026-05-04", "2026-05-04"), Lisbon).ShouldBe(minutes);
    }

    [Fact]
    public void A_90_minute_pitch_open_all_day_loses_the_remainder_of_the_day()
    {
        var allDay = Schedule(90, Enum.GetValues<DayOfWeek>().Select(day => (day, "00:00", "00:00")).ToArray());

        // 24 h = 16 slots of 90 minutes; 25 h = 16 slots (1500 / 90 = 16.67); 23 h = 15 slots (1380 / 90 = 15.33).
        OccupationService.OpenMinutes(allDay, SlotDuration.NinetyMinutes, Period("2026-05-04", "2026-05-04"), Lisbon).ShouldBe(16 * 90);
        OccupationService.OpenMinutes(allDay, SlotDuration.NinetyMinutes, Period("2026-10-25", "2026-10-25"), Lisbon).ShouldBe(16 * 90);
        OccupationService.OpenMinutes(allDay, SlotDuration.NinetyMinutes, Period("2026-03-29", "2026-03-29"), Lisbon).ShouldBe(15 * 90);
    }

    [Fact]
    public void Hours_across_the_march_gap_are_shortened_by_the_hour_that_does_not_exist()
    {
        // Sunday 29 March 2026: 01:00-03:00 local is one real hour (01:00 WET is 01:00 UTC, 03:00 WEST is 02:00 UTC).
        var night = Schedule((DayOfWeek.Sunday, "01:00", "03:00"));

        OccupationService.OpenMinutes(night, SlotDuration.SixtyMinutes, Period("2026-03-29", "2026-03-29"), Lisbon).ShouldBe(60);
    }

    [Fact]
    public void The_opening_interval_is_the_one_the_slots_use()
    {
        var hours = OpeningHours.Create(new TimeOnly(0, 0), new TimeOnly(0, 0));

        var (opensAt, closesAt) = hours.UtcIntervalOn(new DateOnly(2026, 10, 25), Lisbon);
        var slots = SlotGenerator.Generate(new DateOnly(2026, 10, 25), hours, SlotDuration.FromMinutes(60), Lisbon);

        opensAt.ShouldBe(new DateTime(2026, 10, 24, 23, 0, 0, DateTimeKind.Utc));
        closesAt.ShouldBe(new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc));
        slots.Count.ShouldBe(25);
        slots[0].StartsAtUtc.ShouldBe(opensAt);
        slots[^1].EndsAtUtc.ShouldBe(closesAt);
    }

    // ---- The figures

    [Fact]
    public void Occupation_is_booked_over_open_and_zero_when_nothing_is_open()
    {
        new OccupationFigures(180, 240, 80m, 3, 1).Occupation.ShouldBe(0.75m);
        new OccupationFigures(90, 0, 20m, 1, 0).Occupation.ShouldBe(0m);
        new OccupationFigures(270, 660, 0m, 0, 0).Occupation.ShouldBe(0.4091m);
    }

    [Fact]
    public void Hours_are_rounded_to_two_decimals_and_totals_add_minutes_not_rates()
    {
        var a = new OccupationFigures(180, 240, 80m, 3, 1);
        var b = new OccupationFigures(90, 480, 20m, 1, 0);

        var total = OccupationFigures.Sum([a, b]);

        total.BookedHours.ShouldBe(4.5m);
        total.OpenHours.ShouldBe(12m);
        total.Occupation.ShouldBe(0.375m);
        total.Revenue.ShouldBe(100m);
        total.ConfirmedBookings.ShouldBe(4);
        total.NoShows.ShouldBe(1);
        new OccupationFigures(20, 60, 0m, 0, 0).BookedHours.ShouldBe(0.33m);
    }

    // ---- The period

    [Fact]
    public void Without_dates_the_period_is_the_last_30_days()
    {
        var period = StatisticsPeriodInput.Parse(null, null, Today);

        period.IsSuccess.ShouldBeTrue();
        period.Value.ShouldBe(new StatisticsPeriod(new DateOnly(2026, 4, 14), Today));
    }

    [Fact]
    public void A_period_of_one_day_and_one_of_366_days_are_allowed()
    {
        StatisticsPeriodInput.Parse("2026-05-04", "2026-05-04", Today).IsSuccess.ShouldBeTrue();
        StatisticsPeriodInput.Parse("2024-01-01", "2024-12-31", Today).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("2026-05-10", "2026-05-04", "to")]
    [InlineData("2025-01-01", "2026-01-02", "to")]
    [InlineData("hoje", null, "from")]
    [InlineData(null, "2101-01-01", "to")]
    [InlineData("1999-12-31", null, "from")]
    [InlineData("2026-06-01", null, "from")]
    public void A_wrong_period_is_refused_on_its_field(string? from, string? to, string field)
    {
        var period = StatisticsPeriodInput.Parse(from, to, Today);

        period.IsSuccess.ShouldBeFalse();
        period.Error!.FieldErrors!.Keys.ShouldContain(field);
    }

    [Fact]
    public void The_instants_of_a_period_are_the_lisbon_midnights()
    {
        var period = Period("2026-10-25", "2026-10-25");

        period.FromUtc(Lisbon).ShouldBe(new DateTime(2026, 10, 24, 23, 0, 0, DateTimeKind.Utc));
        period.ToUtcExclusive(Lisbon).ShouldBe(new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc));
        period.Days().ShouldBe([new DateOnly(2026, 10, 25)]);
    }
}
