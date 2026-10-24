namespace SportsBooking.Domain.Pitches;

/// <summary>The fixed list of sports a pitch can be for (US07).</summary>
public enum Sport
{
    Futsal,
    Football7,
    Padel,
    Tennis,
}

/// <summary>
/// The stable codes of <see cref="Sport"/> used in the API and stored as text. They never change, whatever the label
/// the UI shows (futsal, futebol 7, padel, ténis).
/// </summary>
public static class Sports
{
    public static IReadOnlyList<Sport> All { get; } = [Sport.Futsal, Sport.Football7, Sport.Padel, Sport.Tennis];

    public static IReadOnlyList<string> Codes { get; } = All.Select(ToCode).ToList();

    public static string ToCode(Sport sport) => sport switch
    {
        Sport.Futsal => "futsal",
        Sport.Football7 => "football7",
        Sport.Padel => "padel",
        Sport.Tennis => "tennis",
        _ => throw new ArgumentOutOfRangeException(nameof(sport), sport, "Unknown sport."),
    };

    public static bool TryParse(string? code, out Sport sport)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(ToCode(candidate), code, StringComparison.Ordinal))
            {
                sport = candidate;
                return true;
            }
        }

        sport = default;
        return false;
    }
}
