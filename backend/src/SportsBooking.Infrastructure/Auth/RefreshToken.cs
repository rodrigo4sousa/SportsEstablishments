using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportsBooking.Infrastructure.Identity;

namespace SportsBooking.Infrastructure.Auth;

/// <summary>
/// One refresh token of a session. The value itself is only ever in the user's cookie; the database keeps
/// its SHA-256 hash. A token is used once: refreshing revokes it and issues the next one.
/// </summary>
public sealed class RefreshToken
{
    private const int ValueBytes = 32;

    private RefreshToken()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public DateTime CreatedAt { get; private set; }

    public DateTime ExpiresAt { get; private set; }

    /// <summary>When the token was used or revoked, in UTC; null while it can still be used.</summary>
    public DateTime? RevokedAt { get; private set; }

    /// <summary>Creates a token and returns it with the value to put in the cookie.</summary>
    public static (RefreshToken Token, string Value) Issue(Guid userId, DateTime now, DateTime expiresAt)
    {
        var value = Base64UrlEncode(RandomNumberGenerator.GetBytes(ValueBytes));
        var token = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = Hash(value),
            CreatedAt = now,
            ExpiresAt = expiresAt,
        };
        return (token, value);
    }

    public static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    public bool IsExpired(DateTime now) => now >= ExpiresAt;

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.TokenHash).IsRequired();
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => token.UserId);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(token => token.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
