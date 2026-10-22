namespace SportsBooking.Domain.Scheduling;

public static class ClubTimeZones
{
    /// <summary>IANA id of the time zone every club currently uses.</summary>
    public const string LisbonId = "Europe/Lisbon";

    public static TimeZoneInfo Lisbon { get; } = TimeZoneInfo.FindSystemTimeZoneById(LisbonId);
}
