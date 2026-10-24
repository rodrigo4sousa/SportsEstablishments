namespace SportsBooking.Domain.Scheduling;

/// <summary>A bookable interval [start, end) as UTC instants.</summary>
public sealed record TimeSlot(DateTime StartsAtUtc, DateTime EndsAtUtc)
{
    /// <summary>A slot that starts exactly now already counts as started: it can no longer be booked (US17, US18).</summary>
    public bool HasStartedBy(DateTime nowUtc) => StartsAtUtc <= nowUtc;
}
