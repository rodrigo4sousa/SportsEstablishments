using Microsoft.AspNetCore.Identity;

namespace SportsBooking.Infrastructure.Identity;

/// <summary>An account. The email is also the user name and is unique across all profiles.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public AppUser()
    {
        Id = Guid.CreateVersion7();
    }

    public AppUser(string email, string name, DateTime registeredAt)
        : this()
    {
        UserName = email;
        Email = email;
        Name = name;
        RegisteredAt = registeredAt.Kind == DateTimeKind.Utc ? registeredAt : throw new ArgumentException("Times must be UTC.", nameof(registeredAt));
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>When the account was created, in UTC (registration, or the seed): what the admin's lists and charts of sign-ups read (US26, US28).</summary>
    public DateTime RegisteredAt { get; private set; }

    /// <summary>When the account was deactivated (US29), in UTC; null while active.</summary>
    public DateTime? DeactivatedAt { get; private set; }

    /// <summary>The version of the Terms and the Privacy Policy accepted at registration (T07); null for accounts made without that step (older ones, the seeds).</summary>
    public string? AcceptedTermsVersion { get; private set; }

    /// <summary>When that version was accepted, in UTC; null exactly when <see cref="AcceptedTermsVersion"/> is.</summary>
    public DateTime? TermsAcceptedAt { get; private set; }

    public bool IsActive => DeactivatedAt is null;

    /// <summary>T07: records what the person accepted when signing up.</summary>
    public void RecordTermsAcceptance(string version, DateTime acceptedAt)
    {
        AcceptedTermsVersion = version;
        TermsAcceptedAt = acceptedAt.Kind == DateTimeKind.Utc ? acceptedAt : throw new ArgumentException("Times must be UTC.", nameof(acceptedAt));
    }

    /// <summary>A deactivated account gets no new session tokens (Épico 1); an issued access token lasts until it expires.</summary>
    public void Deactivate(DateTime now)
    {
        DeactivatedAt ??= now;
    }
}
