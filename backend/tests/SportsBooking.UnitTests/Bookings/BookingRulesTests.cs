using SportsBooking.Application.Bookings;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Bookings;
using SportsBooking.Domain.Pitches;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.UnitTests.Bookings;

/// <summary>US18: the booking request, the slot-matching rule and the booking entity.</summary>
public sealed class BookingRulesTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Lisbon = ClubTimeZones.Lisbon;

    private static IReadOnlyDictionary<string, string[]> Errors(CreateBookingRequest request) =>
        new CreateBookingRequestValidator().Validate(request).ToFieldErrors();

    private static Pitch PitchWith(int minutes, DayOfWeek day, TimeOnly opens, TimeOnly closes)
    {
        var pitch = Pitch.Create(Guid.CreateVersion7(), new PitchDetails("Campo 1", Sport.Padel, 20m), Now);
        var week = PitchScheduleRules.Week.ToDictionary(weekday => weekday, _ => (OpeningHours?)null);
        week[day] = OpeningHours.Create(opens, closes);
        pitch.ChangeSchedule(SlotDuration.FromMinutes(minutes), week, Now);
        return pitch;
    }

    private static DateTime Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    // ---- Request

    [Theory]
    [InlineData("2026-05-04T08:00:00Z", 8, 0)]
    [InlineData("2026-05-04T09:00:00+01:00", 8, 0)]
    [InlineData("2026-05-04T08:00Z", 8, 0)]
    [InlineData("2026-05-04T08:00:00.000Z", 8, 0)]
    [InlineData(" 2026-05-04T10:30:00+02:00 ", 8, 30)]
    public void A_start_with_its_zone_is_read_as_a_utc_instant(string startsAt, int hour, int minute)
    {
        var request = new CreateBookingRequest(Guid.CreateVersion7(), startsAt);

        Errors(request).ShouldBeEmpty();
        request.StartsAtUtc.ShouldBe(Utc(5, 4, hour, minute));
        request.StartsAtUtc!.Value.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData("2026-05-04T08:00:00")] // no zone: would depend on the server
    [InlineData("2026-05-04 08:00:00Z")]
    [InlineData("2026-05-04")]
    [InlineData("04/05/2026 08:00")]
    [InlineData("2026-02-30T08:00:00Z")]
    [InlineData("amanha")]
    public void Anything_else_is_rejected_on_starts_at(string startsAt)
    {
        Errors(new CreateBookingRequest(Guid.CreateVersion7(), startsAt))["startsAt"]
            .ShouldBe(["Indica o início do slot como instante com fuso, por exemplo 2026-10-25T09:00:00Z."]);
    }

    [Fact]
    public void Both_fields_are_required()
    {
        var errors = Errors(new CreateBookingRequest(Guid.Empty, null!));

        errors["pitchId"].ShouldBe(["Indica o campo a reservar."]);
        errors["startsAt"].ShouldBe(["Indica o início do slot."]);
    }

    // ---- Slot matching

    [Fact]
    public void A_start_matches_the_generated_slot_and_takes_its_end_from_the_schedule()
    {
        var pitch = PitchWith(90, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(13, 0));

        // 10:30 WEST on Monday 4 May.
        var slot = pitch.SlotStartingAt(Utc(5, 4, 9, 30), Lisbon).ShouldNotBeNull();

        slot.EndsAtUtc.ShouldBe(Utc(5, 4, 11, 0));
    }

    [Theory]
    [InlineData(5, 5, 8, 0)] // Tuesday: closed
    [InlineData(5, 4, 8, 30)] // 09:30: off the 90-minute grid
    [InlineData(5, 4, 7, 0)] // 08:00: before opening
    [InlineData(5, 4, 11, 0)] // 12:00: the 12:00-13:30 block would pass closing, so it is not a slot
    [InlineData(5, 4, 12, 0)] // 13:00: closing time
    public void A_start_that_is_not_a_generated_slot_matches_nothing(int month, int day, int hour, int minute)
    {
        var pitch = PitchWith(90, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(13, 0));

        pitch.SlotStartingAt(Utc(month, day, hour, minute), Lisbon).ShouldBeNull();
    }

    [Fact]
    public void Seconds_off_the_start_match_nothing()
    {
        var pitch = PitchWith(60, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(13, 0));

        pitch.SlotStartingAt(Utc(5, 4, 8).AddSeconds(1), Lisbon).ShouldBeNull();
    }

    [Fact]
    public void The_slot_belongs_to_the_local_date_of_its_start_even_when_the_utc_date_is_the_day_before()
    {
        // Sunday 25 October open from midnight: 00:00 WEST is 23:00 UTC on Saturday the 24th.
        var pitch = PitchWith(60, DayOfWeek.Sunday, new TimeOnly(0, 0), new TimeOnly(4, 0));

        var slot = pitch.SlotStartingAt(new DateTime(2026, 10, 24, 23, 0, 0, DateTimeKind.Utc), Lisbon).ShouldNotBeNull();

        slot.EndsAtUtc.ShouldBe(new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Both_slots_of_the_repeated_october_hour_match_by_their_instant()
    {
        var pitch = PitchWith(60, DayOfWeek.Sunday, new TimeOnly(0, 0), new TimeOnly(4, 0));

        pitch.SlotStartingAt(new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc), Lisbon).ShouldNotBeNull().EndsAtUtc
            .ShouldBe(new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc));
        pitch.SlotStartingAt(new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc), Lisbon).ShouldNotBeNull().EndsAtUtc
            .ShouldBe(new DateTime(2026, 10, 25, 2, 0, 0, DateTimeKind.Utc));
    }

    // ---- The booking

    [Fact]
    public void A_player_booking_is_confirmed_with_its_price_and_a_v7_id()
    {
        var pitchId = Guid.CreateVersion7();
        var playerId = Guid.CreateVersion7();

        var booking = Booking.ForPlayer(pitchId, playerId, Utc(5, 4, 8), Utc(5, 4, 9), 20m, Now);

        booking.Id.Version.ShouldBe(7);
        booking.PitchId.ShouldBe(pitchId);
        booking.PlayerId.ShouldBe(playerId);
        booking.Price.ShouldBe(20m);
        booking.Status.ShouldBe(BookingStatus.Confirmed);
        booking.IsConfirmed.ShouldBeTrue();
        booking.CreatedAt.ShouldBe(Now);
        BookingStatuses.ToCode(booking.Status).ShouldBe("confirmed");
    }

    [Fact]
    public void A_booking_refuses_bad_values()
    {
        Should.Throw<ArgumentException>(() => Booking.ForPlayer(Guid.CreateVersion7(), Guid.Empty, Utc(5, 4, 8), Utc(5, 4, 9), 20m, Now));
        Should.Throw<ArgumentException>(() => Booking.ForPlayer(Guid.CreateVersion7(), Guid.CreateVersion7(), Utc(5, 4, 9), Utc(5, 4, 9), 20m, Now));
        Should.Throw<ArgumentOutOfRangeException>(() => Booking.ForPlayer(Guid.CreateVersion7(), Guid.CreateVersion7(), Utc(5, 4, 8), Utc(5, 4, 9), 0m, Now));
        Should.Throw<ArgumentException>(() => Booking.ForPlayer(Guid.CreateVersion7(), Guid.CreateVersion7(), Utc(5, 4, 8).ToLocalTime(), Utc(5, 4, 9), 20m, Now));
    }

    [Theory]
    [InlineData(7, 9, true)]
    [InlineData(8, 9, true)]
    [InlineData(8, 30, true)]
    [InlineData(9, 10, false)] // touching
    [InlineData(7, 8, false)] // touching
    public void Overlap_is_half_open_like_the_database_ranges(int fromHour, int toHour, bool overlaps)
    {
        var booking = Booking.ForPlayer(Guid.CreateVersion7(), Guid.CreateVersion7(), Utc(5, 4, 8), Utc(5, 4, 9), 20m, Now);
        var to = toHour == 30 ? Utc(5, 4, 8, 30) : Utc(5, 4, toHour);

        booking.Overlaps(Utc(5, 4, fromHour), to).ShouldBe(overlaps);
    }

    [Fact]
    public void Status_codes_are_stable()
    {
        Enum.GetValues<BookingStatus>().Select(BookingStatuses.ToCode).ShouldBe(["confirmed", "cancelled_by_player", "cancelled_by_club", "no_show"]);
    }
}
