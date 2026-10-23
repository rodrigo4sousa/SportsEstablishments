using Microsoft.AspNetCore.Identity;

namespace SportsBooking.Infrastructure.Identity;

/// <summary>An account. The email is also the user name and is unique across all profiles.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public AppUser()
    {
        Id = Guid.CreateVersion7();
    }

    public AppUser(string email, string name)
        : this()
    {
        UserName = email;
        Email = email;
        Name = name;
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>When the account was deactivated (US29), in UTC; null while active.</summary>
    public DateTime? DeactivatedAt { get; private set; }

    public bool IsActive => DeactivatedAt is null;

    /// <summary>A deactivated account gets no new session tokens (Épico 1); an issued access token lasts until it expires.</summary>
    public void Deactivate(DateTime now)
    {
        DeactivatedAt ??= now;
    }
}
