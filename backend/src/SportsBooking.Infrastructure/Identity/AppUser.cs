using Microsoft.AspNetCore.Identity;

namespace SportsBooking.Infrastructure.Identity;

/// <summary>An account. The email is also the user name and is unique across all profiles.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public AppUser()
    {
        Id = Guid.CreateVersion7();
    }

    public AppUser(string email)
        : this()
    {
        UserName = email;
        Email = email;
    }
}
