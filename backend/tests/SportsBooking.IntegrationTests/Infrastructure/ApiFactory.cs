using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>The access token signing key of the test host.</summary>
    public const string SigningKey = "integration-tests-signing-key-0123456789";

    // Many hosts run at once, each with its own connection pool: a larger server limit than the default 100 leaves room for them.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresImage).WithCommand("-c", "max_connections=400").Build();

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
            // A small pool that gives idle connections back quickly: every isolated host has its own pool, and a few dozen of them (some
            // disposed, with connections still idle) used to use up the server's connections and fail unrelated tests.
            builder.UseSetting("ConnectionStrings:Default", ConnectionStringFor(databaseName) + ";Maximum Pool Size=8;Minimum Pool Size=0;Connection Idle Lifetime=15");
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
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        // US22: no SMTP server in tests. The worker is off; the tests that need it run it themselves.
        builder.UseSetting("Email:Host", "localhost");
        builder.UseSetting("Email:Port", "2525");
        builder.UseSetting("Email:Security", "None");
        builder.UseSetting("Email:FromAddress", "reservas@testes.invalid");
        builder.UseSetting("Outbox:Enabled", "false");
        // Every test request shares one address; rate limiting is tested on its own app (RateLimitingTests).
        builder.UseSetting("RateLimiting:Public:ReadPermitLimit", "100000");
        builder.UseSetting("RateLimiting:Auth:LoginPermitLimit", "100000");
        builder.UseSetting("RateLimiting:Auth:RegisterPermitLimit", "100000");
        builder.UseSetting("RateLimiting:Auth:RefreshPermitLimit", "100000");
        builder.UseSetting("RateLimiting:Auth:PasswordResetRequestPermitLimit", "100000");
        builder.UseSetting("RateLimiting:Auth:PasswordResetConfirmPermitLimit", "100000");
        builder.UseSetting("PublicSite:BaseUrl", SiteOrigin);
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter, TestClientAddress>();
            services.AddControllers().AddApplicationPart(typeof(ProfileAreasTestController).Assembly);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
