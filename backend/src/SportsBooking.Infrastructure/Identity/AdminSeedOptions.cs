namespace SportsBooking.Infrastructure.Identity;

/// <summary>
/// Admin account created at startup (US04). Set through environment variables
/// <c>AdminSeed__Email</c> and <c>AdminSeed__Password</c>; never commit real values.
/// </summary>
public sealed class AdminSeedOptions
{
    public const string SectionName = "AdminSeed";

    public string? Email { get; set; }

    public string? Password { get; set; }
}
