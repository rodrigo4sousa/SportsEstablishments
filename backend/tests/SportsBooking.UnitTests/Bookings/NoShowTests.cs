using SportsBooking.Domain.Bookings;

namespace SportsBooking.UnitTests.Bookings;

/// <summary>A confirmed booking whose end has passed can be marked as a no-show (the groundwork of US15, used by the demo data).</summary>
public sealed class NoShowTests
{
    private static readonly DateTime Start = new(2026, 5, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Created = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);

    private static Booking Confirmed() => Booking.ForPlayer(Guid.CreateVersion7(), Guid.CreateVersion7(), Start, Start.AddHours(1), 20m, Created);

    [Theory]
    [InlineData(60)] // exactly at the end
    [InlineData(61)]
    [InlineData(60 * 24)]
    public void A_confirmed_booking_that_has_ended_becomes_a_no_show(int minutesAfterStart)
    {
        var booking = Confirmed();

        booking.MarkNoShow(Start.AddMinutes(minutesAfterStart)).ShouldBeTrue();

        booking.Status.ShouldBe(BookingStatus.NoShow);
        booking.IsConfirmed.ShouldBeFalse();
        booking.StartsAt.ShouldBe(Start);
        booking.Price.ShouldBe(20m);
    }

    [Theory]
    [InlineData(-60)]
    [InlineData(0)]
    [InlineData(30)] // in progress
    [InlineData(59)]
    public void A_booking_that_has_not_ended_is_not_a_no_show_and_stays_confirmed(int minutesAfterStart)
    {
        var booking = Confirmed();

        booking.MarkNoShow(Start.AddMinutes(minutesAfterStart)).ShouldBeFalse();

        booking.Status.ShouldBe(BookingStatus.Confirmed);
    }

    [Fact]
    public void A_cancelled_booking_is_not_a_no_show()
    {
        var byPlayer = Confirmed();
        byPlayer.CancelByPlayer(TimeSpan.FromHours(24), Created);
        var byClub = Confirmed();
        byClub.CancelByClub("Avaria", Created);

        byPlayer.MarkNoShow(Start.AddDays(1)).ShouldBeFalse();
        byClub.MarkNoShow(Start.AddDays(1)).ShouldBeFalse();

        byPlayer.Status.ShouldBe(BookingStatus.CancelledByPlayer);
        byClub.Status.ShouldBe(BookingStatus.CancelledByClub);
    }

    [Fact]
    public void A_customer_booking_can_be_a_no_show_too()
    {
        var booking = Booking.ForCustomer(Guid.CreateVersion7(), "Maria Cliente", "912345678", Start, Start.AddHours(1), 20m, Created);

        booking.MarkNoShow(Start.AddHours(2)).ShouldBeTrue();

        booking.Status.ShouldBe(BookingStatus.NoShow);
    }

    [Fact]
    public void Marking_it_twice_changes_nothing_the_second_time()
    {
        var booking = Confirmed();
        booking.MarkNoShow(Start.AddHours(2)).ShouldBeTrue();

        booking.MarkNoShow(Start.AddHours(3)).ShouldBeFalse();

        booking.Status.ShouldBe(BookingStatus.NoShow);
    }

    [Fact]
    public void Times_must_be_utc()
    {
        Should.Throw<ArgumentException>(() => Confirmed().MarkNoShow(new DateTime(2026, 5, 7, 10, 0, 0, DateTimeKind.Unspecified)));
    }
}
