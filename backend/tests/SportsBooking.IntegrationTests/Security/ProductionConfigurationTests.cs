using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Npgsql;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Security;

/// <summary>T06: how the API behaves in the Production environment: what stops it from starting, what it never does at startup, and what it survives.</summary>
public sealed class ProductionConfigurationTests(ApiFactory factory)
{
    private const string VercelSite = "https://reservas.vercel.app";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> ProductionApp(IReadOnlyDictionary<string, string?>? settings = null, string? database = null) =>
        factory.CreateIsolatedApp(database ?? $"t06_prod_{Guid.NewGuid():N}", settings).WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

    [Theory]
    [InlineData("Jwt:SigningKey")]
    [InlineData("ConnectionStrings:Default")]
    [InlineData("Security:AllowedOrigins:0")]
    public void A_missing_required_setting_stops_the_api_and_the_error_names_it(string setting)
    {
        using var app = ProductionApp(new Dictionary<string, string?> { [setting] = null });

        var failure = Should.Throw<OptionsValidationException>(() => _ = app.Server);

        failure.Message.ShouldContain(setting.Replace(":0", string.Empty, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(VercelSite, HttpStatusCode.Unauthorized)]
    [InlineData("https://sportsbooking-api.onrender.com", HttpStatusCode.Forbidden)]
    [InlineData("https://reservas-git-branch.vercel.app", HttpStatusCode.Forbidden)]
    [InlineData(null, HttpStatusCode.Forbidden)]
    public async Task Behind_the_proxy_only_the_sites_own_origin_can_mutate(string? origin, HttpStatusCode expected)
    {
        await using var app = ProductionApp(new Dictionary<string, string?> { ["Security:AllowedOrigins:0"] = VercelSite });
        using var client = app.CreateClient(new() { BaseAddress = new Uri(ApiFactory.SiteOrigin) });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/origin-check-probe") { Content = JsonContent.Create(new { }) };
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        using var response = await client.SendAsync(request, Ct);

        // A request that passes the check is then a 401: no session.
        response.StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task Migrations_never_run_at_startup_in_production_even_with_the_setting_on()
    {
        var database = $"t06_nomig_{Guid.NewGuid():N}";
        await using var app = ProductionApp(new Dictionary<string, string?> { ["Database:ApplyMigrationsOnStartup"] = "true" }, database);

        _ = app.Server;

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = @name", connection);
        command.Parameters.AddWithValue("name", database);
        ((long)(await command.ExecuteScalarAsync(Ct))!).ShouldBe(0);
    }

    [Fact]
    public async Task Health_is_liveness_only_and_answers_with_the_database_unreachable()
    {
        await using var app = ProductionApp(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1",
        });
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/health", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("Healthy");
    }
}
