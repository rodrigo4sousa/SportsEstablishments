using SportsBooking.Domain.Bookings;

namespace SportsBooking.UnitTests.Bookings;

/// <summary>US19: when a player can cancel a booking, and what cancelling does to it.</summary>
public sealed class BookingCancellationTests
{
    private static readonly DateTime Start = new(2026, 5, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Created = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    private static Booking Confirmed() => Booking.ForPlayer(Guid.CreateVersion7(), Guid.CreateVersion7(), Start, Start.AddHours(1), 20m, Created);

    [Fact]
    public void The_deadline_is_the_start_minus_the_window()
    {
        Confirmed().CancellationDeadline(Day).ShouldBe(new DateTime(2026, 5, 5, 10, 0, 0, DateTimeKind.Utc));
        Confirmed().CancellationDeadline(TimeSpan.FromHours(168)).ShouldBe(new DateTime(2026, 4, 29, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void A_confirmed_booking_is_cancelled_by_the_player_and_kept()
    {
        var booking = Confirmed();

        var outcome = booking.CancelByPlayer(Day, Created);

        outcome.ShouldBe(CancellationOutcome.Cancelled);
        booking.Status.ShouldBe(BookingStatus.CancelledByPlayer);
        booking.IsConfirmed.ShouldBeFalse();
        // Nothing else changes: it is history, with the price it had.
        booking.StartsAt.ShouldBe(Start);
        booking.EndsAt.ShouldBe(Start.AddHours(1));
        booking.Price.ShouldBe(20m);
    }

    [Fact]
    public void Cancelling_is_allowed_up_to_the_last_tick_before_the_deadline()
    {
        var deadline = Confirmed().CancellationDeadline(Day);

        var justBefore = Confirmed();
        justBefore.CanBeCancelledByPlayer(Day, deadline.AddTicks(-1)).ShouldBeTrue();
        justBefore.CancelByPlayer(Day, deadline.AddTicks(-1)).ShouldBe(CancellationOutcome.Cancelled);
    }

    [Theory]
    [InlineData(0)] // exactly at the deadline: provisional, "strictly before"
    [InlineData(1)]
    [InlineData(60)] // an hour after the deadline
    public void At_the_deadline_and_after_it_the_player_can_no_longer_cancel(int minutesAfterDeadline)
    {
        var booking = Confirmed();
        var now = booking.CancellationDeadline(Day).AddMinutes(minutesAfterDeadline);

        booking.CanBeCancelledByPlayer(Day, now).ShouldBeFalse();
        booking.CancelByPlayer(Day, now).ShouldBe(CancellationOutcome.OutsideWindow);
        booking.Status.ShouldBe(BookingStatus.Confirmed);
    }

    [Fact]
    public void A_booking_that_has_started_or_ended_cannot_be_cancelled()
    {
        var booking = Confirmed();

        booking.CancelByPlayer(Day, Start).ShouldBe(CancellationOutcome.OutsideWindow);
        booking.CancelByPlayer(Day, Start.AddHours(2)).ShouldBe(CancellationOutcome.OutsideWindow);
        booking.Status.ShouldBe(BookingStatus.Confirmed);
    }

    [Fact]
    public void A_booking_cancelled_already_is_not_cancelled_again()
    {
        var booking = Confirmed();
        booking.CancelByPlayer(Day, Created).ShouldBe(CancellationOutcome.Cancelled);

        booking.CancelByPlayer(Day, Created).ShouldBe(CancellationOutcome.NotConfirmed);
        booking.CanBeCancelledByPlayer(Day, Created).ShouldBeFalse();
        booking.Status.ShouldBe(BookingStatus.CancelledByPlayer);
    }

    [Fact]
    public void The_window_is_a_parameter_so_a_changed_window_applies_to_existing_bookings()
    {
        var booking = Confirmed();
        var thirtyHoursBefore = Start.AddHours(-30);

        booking.CanBeCancelledByPlayer(Day, thirtyHoursBefore).ShouldBeTrue(); // 24 h window
        booking.CanBeCancelledByPlayer(TimeSpan.FromHours(48), thirtyHoursBefore).ShouldBeFalse(); // the club widened it
        booking.CanBeCancelledByPlayer(TimeSpan.FromHours(1), Start.AddHours(-2)).ShouldBeTrue(); // or narrowed it
    }

    [Fact]
    public void Times_must_be_utc_and_the_window_cannot_be_negative()
    {
        var booking = Confirmed();

        Should.Throw<ArgumentException>(() => booking.CancelByPlayer(Day, DateTime.SpecifyKind(Created, DateTimeKind.Local)));
        Should.Throw<ArgumentOutOfRangeException>(() => booking.CancellationDeadline(TimeSpan.FromHours(-1)));
    }
}
