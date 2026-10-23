using Microsoft.Net.Http.Headers;

namespace SportsBooking.IntegrationTests.Infrastructure;

/// <summary>Reads the session cookies a response sets, with their attributes.</summary>
public static class SessionCookies
{
    public const string AccessToken = "access_token";
    public const string RefreshToken = "refresh_token";

    public static IReadOnlyDictionary<string, SetCookieHeaderValue> SetCookies(this HttpResponseMessage response) =>
        response.Headers.TryGetValues(HeaderNames.SetCookie, out var values)
            ? SetCookieHeaderValue.ParseList(values.ToList()).ToDictionary(cookie => cookie.Name.ToString())
            : new Dictionary<string, SetCookieHeaderValue>();

    /// <summary>A request that carries the given cookies, as a browser would send them.</summary>
    public static HttpRequestMessage WithCookies(this HttpRequestMessage request, params (string Name, string Value)[] cookies)
    {
        request.Headers.Add(HeaderNames.Cookie, string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}")));
        return request;
    }

    /// <summary>A client on the site's HTTPS origin that sends the site's Origin but keeps no cookie jar.</summary>
    public static HttpClient CreateSiteClient(this ApiFactory factory)
    {
        var client = factory.CreateClient(new() { BaseAddress = new Uri(ApiFactory.SiteOrigin), HandleCookies = false });
        client.DefaultRequestHeaders.Add(HeaderNames.Origin, ApiFactory.SiteOrigin);
        return client;
    }
}
