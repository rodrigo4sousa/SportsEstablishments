using Microsoft.AspNetCore.Mvc.Testing;

namespace SportsBooking.IntegrationTests.Infrastructure;

public static class BrowserClient
{
    /// <summary>
    /// A client that behaves like a browser on the site: HTTPS (so Secure cookies are kept and sent),
    /// a cookie jar, and the site's own <c>Origin</c> on every request.
    /// </summary>
    public static HttpClient CreateBrowserClient(this WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(ApiFactory.SiteOrigin),
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add("Origin", ApiFactory.SiteOrigin);
        return client;
    }
}
