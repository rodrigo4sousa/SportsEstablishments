using Microsoft.Extensions.Options;

namespace SportsBooking.Api.Security;

/// <summary>
/// T06: requests allowed per client IP and per window on the public read endpoints (club search, localities, a club's page and its
/// slots), one limit shared by all four. Provisional: 120 a minute is two a second, well above normal browsing (a search, a few
/// clubs, a date or pitch change each) and well below scraping. Settings <c>RateLimiting:Public:*</c>, e.g. env
/// <c>RateLimiting__Public__ReadPermitLimit</c>.
/// </summary>
internal sealed class PublicRateLimitOptions
{
    public const string SectionName = "RateLimiting:Public";

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    public int ReadPermitLimit { get; set; } = 120;
}

internal sealed class PublicRateLimitOptionsValidator : IValidateOptions<PublicRateLimitOptions>
{
    public ValidateOptionsResult Validate(string? name, PublicRateLimitOptions options)
    {
        var failures = new List<string>();
        if (options.Window <= TimeSpan.Zero)
        {
            failures.Add("RateLimiting:Public:Window must be a positive time span, e.g. 00:01:00.");
        }

        if (options.ReadPermitLimit < 1)
        {
            failures.Add("RateLimiting:Public:ReadPermitLimit must be at least 1.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
