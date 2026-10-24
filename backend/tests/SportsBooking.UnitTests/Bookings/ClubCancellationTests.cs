using SportsBooking.Domain.Bookings;

namespace SportsBooking.UnitTests.Bookings;

/// <summary>US12: the club's owner cancels a booking that has not started, with a reason and no cancellation window.</summary>
public sealed class ClubCancellationTests
{
    private static readonly DateTime Start = new(2026, 5, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Created = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);

    private static Booking Confirmed() => Booking.ForPlayer(Guid.CreateVersion7(), Guid.CreateVersion7(), Start, Start.AddHours(1), 20m, Created);

    [Fact]
    public void A_confirmed_booking_is_cancelled_by_the_club_with_the_reason_and_the_moment_and_kept()
    {
        var booking = Confirmed();
        var now = Start.AddHours(-1);

        var outcome = booking.CancelByClub("  Campo em obras  ", now);

        outcome.ShouldBe(ClubCancellationOutcome.Cancelled);
        booking.Status.ShouldBe(BookingStatus.CancelledByClub);
        booking.IsConfirmed.ShouldBeFalse();
        booking.CancellationReason.ShouldBe("Campo em obras");
        booking.CancelledAt.ShouldBe(now);
        booking.StartsAt.ShouldBe(Start);
        booking.Price.ShouldBe(20m);
    }

    [Fact]
    public void There_is_no_cancellation_window_for_the_club()
    {
        // One tick before the start: a player's 24-hour window closed long ago, the club may still cancel.
        var booking = Confirmed();

        booking.CancelByClub("Avaria", Start.AddTicks(-1)).ShouldBe(ClubCancellationOutcome.Cancelled);
    }

    [Theory]
    [InlineData(0)] // starting now counts as started
    [InlineData(1)]
    [InlineData(30)] // in progress
    [InlineData(600)] // long over
    public void A_booking_that_has_started_is_refused_and_unchanged(int minutesAfterStart)
    {
        var booking = Confirmed();

        var outcome = booking.CancelByClub("Avaria", Start.AddMinutes(minutesAfterStart));

        outcome.ShouldBe(ClubCancellationOutcome.AlreadyStarted);
        booking.Status.ShouldBe(BookingStatus.Confirmed);
        booking.CancellationReason.ShouldBeNull();
        booking.CancelledAt.ShouldBeNull();
    }

    [Fact]
    public void A_booking_cancelled_already_is_refused_and_keeps_its_first_reason()
    {
        var booking = Confirmed();
        booking.CancelByClub("Primeiro motivo", Created).ShouldBe(ClubCancellationOutcome.Cancelled);

        var second = booking.CancelByClub("Segundo motivo", Created.AddHours(1));

        second.ShouldBe(ClubCancellationOutcome.NotConfirmed);
        booking.CancellationReason.ShouldBe("Primeiro motivo");
        booking.CancelledAt.ShouldBe(Created);
    }

    [Fact]
    public void A_booking_cancelled_by_the_player_cannot_be_cancelled_by_the_club()
    {
        var booking = Confirmed();
        booking.CancelByPlayer(TimeSpan.FromHours(24), Created).ShouldBe(CancellationOutcome.Cancelled);

        booking.CancelByClub("Avaria", Created.AddHours(1)).ShouldBe(ClubCancellationOutcome.NotConfirmed);

        booking.Status.ShouldBe(BookingStatus.CancelledByPlayer);
        booking.CancellationReason.ShouldBeNull();
    }

    [Fact]
    public void A_player_cancellation_stores_when_it_happened_and_no_reason()
    {
        var booking = Confirmed();

        booking.CancelByPlayer(TimeSpan.FromHours(24), Created);

        booking.CancelledAt.ShouldBe(Created);
        booking.CancellationReason.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_reason_is_a_programming_error_because_the_service_validates_it(string reason)
    {
        var booking = Confirmed();

        Should.Throw<ArgumentException>(() => booking.CancelByClub(reason, Created));

        booking.Status.ShouldBe(BookingStatus.Confirmed);
    }

    [Fact]
    public void Times_must_be_utc()
    {
        Should.Throw<ArgumentException>(() => Confirmed().CancelByClub("Avaria", new DateTime(2026, 5, 2, 10, 0, 0, DateTimeKind.Unspecified)));
    }
}
