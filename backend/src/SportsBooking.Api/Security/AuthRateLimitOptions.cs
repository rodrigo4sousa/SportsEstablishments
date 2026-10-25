using Microsoft.Extensions.Options;

namespace SportsBooking.Api.Security;

/// <summary>
/// US03: requests allowed per client IP and per window on each auth endpoint (provisional values, see US03 in
/// docs/backlog.md). Settings <c>RateLimiting:Auth:*</c>, e.g. env <c>RateLimiting__Auth__LoginPermitLimit</c>.
/// </summary>
internal sealed class AuthRateLimitOptions
{
    public const string SectionName = "RateLimiting:Auth";

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    public int LoginPermitLimit { get; set; } = 10;

    public int RegisterPermitLimit { get; set; } = 5;

    /// <summary>Higher: one shared address (a club's Wi-Fi) can hold many sessions that renew every 15 minutes.</summary>
    public int RefreshPermitLimit { get; set; } = 30;

    /// <summary>US05: asking for a reset email (provisional). The per-account limit is separate (PasswordResetRules.ThrottleInterval).</summary>
    public int PasswordResetRequestPermitLimit { get; set; } = 5;

    /// <summary>US05: setting a new password with a link (provisional): low, since each try is a guess at a token.</summary>
    public int PasswordResetConfirmPermitLimit { get; set; } = 10;
}

internal sealed class AuthRateLimitOptionsValidator : IValidateOptions<AuthRateLimitOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthRateLimitOptions options)
    {
        var failures = new List<string>();
        if (options.Window <= TimeSpan.Zero)
        {
            failures.Add("RateLimiting:Auth:Window must be a positive time span, e.g. 00:01:00.");
        }

        foreach (var (setting, limit) in new[]
        {
            (nameof(options.LoginPermitLimit), options.LoginPermitLimit),
            (nameof(options.RegisterPermitLimit), options.RegisterPermitLimit),
            (nameof(options.RefreshPermitLimit), options.RefreshPermitLimit),
            (nameof(options.PasswordResetRequestPermitLimit), options.PasswordResetRequestPermitLimit),
            (nameof(options.PasswordResetConfirmPermitLimit), options.PasswordResetConfirmPermitLimit),
        })
        {
            if (limit < 1)
            {
                failures.Add($"RateLimiting:Auth:{setting} must be at least 1.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
