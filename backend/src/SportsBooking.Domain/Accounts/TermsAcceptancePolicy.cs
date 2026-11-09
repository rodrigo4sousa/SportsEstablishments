namespace SportsBooking.Domain.Accounts;

/// <summary>
/// T07: what a registration must carry to show the person accepted the Terms and the Privacy Policy. The version is the one the site
/// showed; it is kept as sent, so the site owns the list of versions.
/// </summary>
public static class TermsAcceptancePolicy
{
    public const int MaximumVersionLength = 32;

    public static bool IsValidVersion(string? version) =>
        !string.IsNullOrWhiteSpace(version) && version.Length <= MaximumVersionLength;
}
