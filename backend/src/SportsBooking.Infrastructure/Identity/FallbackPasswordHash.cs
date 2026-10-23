using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace SportsBooking.Infrastructure.Identity;

/// <summary>
/// A hash of a random password, made with the configured work factor. Login checks a password against it when there
/// is no real hash to check (unknown email, locked or deactivated account), so every failed login costs the same
/// PBKDF2 work and the response time does not reveal whether the email has an account.
/// </summary>
internal sealed class FallbackPasswordHash(IOptions<PasswordHasherOptions> options)
{
    private readonly PasswordHasher<AppUser> _hasher = new(options);
    private readonly Lazy<string> _hash = new(() =>
        new PasswordHasher<AppUser>(options).HashPassword(new AppUser(), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    /// <summary>Does the work of a password check; the result is always a failure.</summary>
    public void Verify(string password) => _hasher.VerifyHashedPassword(new AppUser(), _hash.Value, password);
}
