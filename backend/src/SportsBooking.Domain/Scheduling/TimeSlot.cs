namespace SportsBooking.Domain.Scheduling;

/// <summary>A bookable interval [start, end) as UTC instants.</summary>
public sealed record TimeSlot(DateTime StartsAtUtc, DateTime EndsAtUtc);
