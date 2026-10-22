using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SportsBooking.Application.Common;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests;

public sealed class SmokeTests(ApiFactory factory)
{
    [Fact]
    public async Task Health_endpoint_reports_healthy_against_real_postgres()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Migrations_are_applied_to_the_database()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var applied = await dbContext.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);
        var pending = await dbContext.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken);

        applied.ShouldContain(migration => migration.EndsWith("_InitialCreate"));
        pending.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_api_route_returns_problem_details_with_stable_code()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("code").GetString().ShouldBe(ErrorCodes.NotFound);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }
}
