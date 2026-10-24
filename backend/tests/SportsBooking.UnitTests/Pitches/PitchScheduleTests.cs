using SportsBooking.Domain.Pitches;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.UnitTests.Pitches;

/// <summary>US08: a pitch's slot length and weekly hours, and the slots they give through SlotGenerator.</summary>
public sealed class PitchScheduleTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc);

    private static Pitch NewPitch() => Pitch.Create(Guid.CreateVersion7(), new PitchDetails("Campo 1", Sport.Padel, 20m), Now);

    private static Dictionary<DayOfWeek, OpeningHours?> AllClosed() => PitchScheduleRules.Week.ToDictionary(day => day, _ => (OpeningHours?)null);

    private static OpeningHours Hours(int opensHour, int opensMinute, int closesHour, int closesMinute) =>
        OpeningHours.Create(new TimeOnly(opensHour, opensMinute), new TimeOnly(closesHour, closesMinute));

    [Fact]
    public void A_new_pitch_is_closed_every_day_with_60_minute_slots()
    {
        var pitch = NewPitch();

        pitch.SlotDurationMinutes.ShouldBe(60);
        pitch.OpeningDays.ShouldBeEmpty();
        foreach (var day in PitchScheduleRules.Week)
        {
            pitch.HoursOn(day).ShouldBeNull();
        }

        pitch.SlotsOn(new DateOnly(2026, 10, 26), ClubTimeZones.Lisbon).ShouldBeEmpty();
    }

    [Fact]
    public void Changing_the_schedule_replaces_the_length_and_the_week()
    {
        var pitch = NewPitch();
        var week = AllClosed();
        week[DayOfWeek.Monday] = Hours(9, 0, 12, 0);
        week[DayOfWeek.Tuesday] = Hours(9, 0, 12, 0);
        pitch.ChangeSchedule(SlotDuration.SixtyMinutes, week, Now);
        var monday = pitch.OpeningDays.Single(day => day.Day == DayOfWeek.Monday);

        week = AllClosed();
        week[DayOfWeek.Monday] = Hours(10, 0, 0, 0);
        week[DayOfWeek.Friday] = Hours(18, 0, 20, 0);
        pitch.ChangeSchedule(SlotDuration.NinetyMinutes, week, Now.AddHours(1));

        pitch.SlotDurationMinutes.ShouldBe(90);
        pitch.HoursOn(DayOfWeek.Monday).ShouldBe(Hours(10, 0, 0, 0));
        pitch.HoursOn(DayOfWeek.Tuesday).ShouldBeNull();
        pitch.HoursOn(DayOfWeek.Friday).ShouldBe(Hours(18, 0, 20, 0));
        pitch.OpeningDays.Count.ShouldBe(2);
        // The open weekday keeps its own row, changed in place.
        pitch.OpeningDays.Single(day => day.Day == DayOfWeek.Monday).ShouldBeSameAs(monday);
        pitch.UpdatedAt.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public void The_week_must_have_every_day()
    {
        var week = AllClosed();
        week.Remove(DayOfWeek.Sunday);

        Should.Throw<ArgumentException>(() => NewPitch().ChangeSchedule(SlotDuration.SixtyMinutes, week, Now));
    }

    [Fact]
    public void Times_off_the_30_minute_step_are_refused_by_the_entity_too()
    {
        var week = AllClosed();
        week[DayOfWeek.Monday] = Hours(9, 15, 12, 0);

        Should.Throw<ArgumentException>(() => NewPitch().ChangeSchedule(SlotDuration.SixtyMinutes, week, Now));
    }

    [Fact]
    public void Slots_come_from_the_weekday_hours_through_the_slot_generator()
    {
        var pitch = NewPitch();
        var week = AllClosed();
        week[DayOfWeek.Monday] = Hours(9, 0, 11, 0);
        pitch.ChangeSchedule(SlotDuration.NinetyMinutes, week, Now);
        var monday = new DateOnly(2026, 5, 4);

        var slots = pitch.SlotsOn(monday, ClubTimeZones.Lisbon);

        slots.ShouldBe(SlotGenerator.Generate(monday, Hours(9, 0, 11, 0), SlotDuration.NinetyMinutes, ClubTimeZones.Lisbon));
        slots.Count.ShouldBe(1);
        pitch.SlotsOn(monday.AddDays(1), ClubTimeZones.Lisbon).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(9, 0, true)]
    [InlineData(9, 30, true)]
    [InlineData(0, 0, true)]
    [InlineData(9, 15, false)]
    [InlineData(9, 45, false)]
    [InlineData(9, 1, false)]
    public void The_time_step_is_30_minutes(int hour, int minute, bool onStep)
    {
        PitchScheduleRules.IsOnStep(new TimeOnly(hour, minute)).ShouldBe(onStep);
    }

    [Fact]
    public void Seconds_are_off_the_step()
    {
        PitchScheduleRules.IsOnStep(new TimeOnly(9, 0, 30)).ShouldBeFalse();
    }

    [Fact]
    public void The_booking_window_is_today_up_to_30_days_ahead()
    {
        var today = new DateOnly(2026, 10, 25);

        BookingWindow.Contains(today, today).ShouldBeTrue();
        BookingWindow.Contains(today.AddDays(30), today).ShouldBeTrue();
        BookingWindow.Contains(today.AddDays(31), today).ShouldBeFalse();
        BookingWindow.Contains(today.AddDays(-1), today).ShouldBeFalse();
    }
}
