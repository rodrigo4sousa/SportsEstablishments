using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Npgsql;
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

    /// <summary>The site's own origin in tests; HTTPS so that Secure cookies round-trip.</summary>
    public const string SiteOrigin = "https://localhost";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresImage).Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        // Starting the host applies migrations and runs the startup seed, as in development.
        _ = Server;
    }

    /// <summary>
    /// A separate app instance on its own fresh database, for tests that depend on startup behaviour.
    /// Logs are captured; read them with <c>app.Services.GetFakeLogCollector()</c>.
    /// </summary>
    public WebApplicationFactory<Program> CreateIsolatedApp(string databaseName, IReadOnlyDictionary<string, string?>? settings = null) =>
        WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", ConnectionStringFor(databaseName));
            foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureLogging(logging => logging.AddFakeLogging());
        });

    public string ConnectionStringFor(string databaseName) =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { Database = databaseName }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("Security:AllowedOrigins:0", SiteOrigin);
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-0123456789");
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
