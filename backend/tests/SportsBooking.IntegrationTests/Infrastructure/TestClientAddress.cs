using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace SportsBooking.IntegrationTests.Infrastructure;

/// <summary>
/// Test host only: the in-memory test server has no network connection, so a request picks its connection address
/// with the <see cref="Header"/> header. It runs before the API's own pipeline, like a real TCP connection.
/// </summary>
public sealed class TestClientAddress : IStartupFilter
{
    public const string Header = "X-Test-Connection-Address";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (IPAddress.TryParse(context.Request.Headers[Header].ToString(), out var address))
            {
                context.Connection.RemoteIpAddress = address;
                context.Request.Headers.Remove(Header);
            }

            await nextMiddleware(context);
        });
        next(app);
    };
}

public static class TestClientAddressExtensions
{
    /// <summary>A request that arrives over a connection from <paramref name="address"/>.</summary>
    public static HttpRequestMessage From(this HttpRequestMessage request, string address)
    {
        request.Headers.Add(TestClientAddress.Header, address);
        return request;
    }
}
