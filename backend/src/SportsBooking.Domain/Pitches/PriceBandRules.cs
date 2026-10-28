namespace SportsBooking.Domain.Pitches;

/// <summary>US10 limits for the price bands of a pitch (the maximum is provisional, see US10 in docs/backlog.md).</summary>
public static class PriceBandRules
{
    /// <summary>At most this many bands per pitch: enough for a week of peak and off-peak hours, and a bound on what every pitch read loads.</summary>
    public const int MaximumPerPitch = 20;

    /// <summary>The price of a band has the limits of the pitch's own price.</summary>
    public static bool IsValidPrice(decimal price) => PitchRules.IsValidPrice(price);
}
