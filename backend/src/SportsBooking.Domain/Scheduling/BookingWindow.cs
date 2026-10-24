namespace SportsBooking.Domain.Scheduling;

/// <summary>Bookings go from today up to 30 days ahead, as dates in the club's time zone (US17, US18).</summary>
public static class BookingWindow
{
    public const int DaysAhead = 30;

    /// <summary>True for <paramref name="today"/> up to and including <paramref name="today"/> + 30 days.</summary>
    public static bool Contains(DateOnly date, DateOnly today) => date >= today && date <= today.AddDays(DaysAhead);
}
