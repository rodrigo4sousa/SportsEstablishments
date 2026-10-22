using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SportsBooking.Infrastructure;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(SportsBooking.IntegrationTests.Infrastructure.ApiFactory))]

namespace SportsBooking.IntegrationTests.Infrastructure;

/// <summary>
/// Runs the real API against a real PostgreSQL in a container, shared by every test in the assembly.
/// The EF Core in-memory provider is never used: it ignores constraints and transactions.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string PostgresImage = "postgres:18.6";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresImage).Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await Services.MigrateDatabaseAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
