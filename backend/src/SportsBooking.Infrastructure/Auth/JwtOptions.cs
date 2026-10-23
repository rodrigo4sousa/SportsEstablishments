using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SportsBooking.Infrastructure.Auth;

/// <summary>
/// Access token signing (HMAC-SHA256). <see cref="SigningKey"/> is a secret: env <c>Jwt__SigningKey</c> in
/// production, never committed; Development has a local-only value in appsettings.Development.json.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HS256 needs a key of at least 256 bits.</summary>
    public const int MinimumSigningKeyBytes = 32;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public string SigningKey { get; set; } = string.Empty;

    internal SymmetricSecurityKey SecurityKey() => new(Encoding.UTF8.GetBytes(SigningKey));

    /// <summary>
    /// The rules every access token is checked against; shared by the issuer and the API's JwtBearer handler.
    /// Expiry is checked by the library against the system clock (it has no public TimeProvider hook).
    /// </summary>
    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = SecurityKey(),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        // Issued and checked by the same API: no clock drift to tolerate beyond the 15 minutes.
        ClockSkew = TimeSpan.Zero,
        NameClaimType = SessionClaims.Subject,
        RoleClaimType = SessionClaims.Role,
    };
}

internal sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add("Jwt:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Jwt:Audience is required.");
        }

        if (Encoding.UTF8.GetByteCount(options.SigningKey ?? string.Empty) < JwtOptions.MinimumSigningKeyBytes)
        {
            failures.Add($"Jwt:SigningKey must have at least {JwtOptions.MinimumSigningKeyBytes} bytes (set it with the Jwt__SigningKey environment variable).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
