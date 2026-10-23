namespace SportsBooking.Domain.Accounts;

/// <summary>
/// The three account profiles. Every account has exactly one.
/// The Admin profile is only ever given by the startup seed (US04), never by an endpoint.
/// </summary>
public static class Profiles
{
    public const string Player = "Player";
    public const string Owner = "Owner";
    public const string Admin = "Admin";

    public static IReadOnlyList<string> All { get; } = [Player, Owner, Admin];

    /// <summary>The profiles a visitor can sign up for (US01, US02). Never Admin.</summary>
    public static IReadOnlyList<string> SelfRegistration { get; } = [Player, Owner];
}
