using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests;

/// <summary>T01: Swagger UI and the OpenAPI document are available in Development without a session.</summary>
public sealed class DevelopmentToolsTests(ApiFactory factory)
{
    [Theory]
    [InlineData("/swagger/index.html")]
    [InlineData("/openapi/v1.json")]
    public async Task Development_tools_need_no_session(string path)
    {
        var databaseName = $"dev_tools_{Guid.NewGuid():N}";
        await using var app = factory.CreateIsolatedApp(databaseName).WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            // Added last, so they win over appsettings.Development.json and any local user-secrets.
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = factory.ConnectionStringFor(databaseName),
                ["AdminSeed:Email"] = string.Empty,
                ["AdminSeed:Password"] = string.Empty,
            }));
        });
        using var client = app.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
