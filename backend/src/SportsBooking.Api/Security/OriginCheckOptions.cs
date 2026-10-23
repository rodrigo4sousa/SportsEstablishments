using Microsoft.Extensions.Options;

namespace SportsBooking.Api.Security;

/// <summary>
/// Origins (scheme, host and port, e.g. <c>https://reservas.pt</c>) allowed to send mutating requests.
/// Set per environment: <c>Security:AllowedOrigins:0</c> (env <c>Security__AllowedOrigins__0</c>).
/// </summary>
internal sealed class OriginCheckOptions
{
    public const string SectionName = "Security";

    public string[] AllowedOrigins { get; set; } = [];
}

internal sealed class OriginCheckOptionsValidator : IValidateOptions<OriginCheckOptions>
{
    public ValidateOptionsResult Validate(string? name, OriginCheckOptions options)
    {
        if (options.AllowedOrigins.Length == 0)
        {
            return ValidateOptionsResult.Fail("Security:AllowedOrigins needs at least one origin (the site's own, e.g. https://reservas.pt).");
        }

        var invalid = options.AllowedOrigins.Where(origin => !IsOrigin(origin)).ToList();
        return invalid.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Security:AllowedOrigins entries must be bare origins like https://reservas.pt (scheme, host, optional port; no path): {string.Join(", ", invalid)}");
    }

    private static bool IsOrigin(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && string.Equals(uri.GetLeftPart(UriPartial.Authority), value, StringComparison.OrdinalIgnoreCase);
}
