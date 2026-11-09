using System.Reflection;
using Microsoft.Extensions.Options;
using SportsBooking.Api.Auth;
using SportsBooking.Api.ErrorHandling;
using SportsBooking.Api.Security;
using SportsBooking.Application;
using SportsBooking.Infrastructure;
using SportsBooking.Infrastructure.Demo;
using SportsBooking.Infrastructure.Identity;

var builder = WebApplication.CreateBuilder(args);

// No `Server: Kestrel` header: it tells a client nothing it needs.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// The build-time OpenAPI generator starts the app without real configuration or a database.
var isOpenApiGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
if (isOpenApiGeneration)
{
    builder.Configuration.AddInMemoryCollection(
    [
        new("ConnectionStrings:Default", "Host=unused-during-openapi-generation"),
        new("Security:AllowedOrigins:0", "https://unused-during-openapi-generation"),
        new("Jwt:SigningKey", "unused-during-openapi-generation-0123456789"),
        new("Email:Host", "unused-during-openapi-generation"),
        new("Email:FromAddress", "unused@openapi.invalid"),
        new("Outbox:Enabled", "false"),
        new("PublicSite:BaseUrl", "https://unused-during-openapi-generation"),
    ]);
}

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

// Missing fields reach FluentValidation (pt-PT messages per field) instead of MVC's implicit [Required].
builder.Services.AddControllers(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);
// Liveness only, with no checks: a database that is paused or down must not make the host restart a process that is fine (T06).
builder.Services.AddHealthChecks();
builder.Services.AddApiProblemDetails();
builder.Services.AddOriginCheck(builder.Configuration);
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddCookieJwtAuthentication();
builder.Services.AddOpenApi(options => options.AddSchemaTransformer<ProblemDetailsSchemaTransformer>());

var app = builder.Build();

// First: the client IP and scheme come from X-Forwarded-For and -Proto only when a proxy is trusted (TrustedProxyOptions). TLS ends at the
// host, so the API speaks HTTP and never redirects to HTTPS. The API is public on Render, so a direct caller can forge these headers: the
// IP-based rate limits are best-effort there, and the Identity account lockout is the real protection against guessing passwords.
app.UseForwardedHeaders();
app.UseResponseSecurityHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseOriginCheck();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    // Swagger UI is middleware, not an endpoint: it must run before the fallback authorization policy.
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "SportsBooking API v1"));
    app.MapOpenApi().AllowAnonymous();
}

app.UseAuthentication();
app.UseAuthorization();

if (!isOpenApiGeneration)
{
    // Invalid settings (e.g. a partial admin seed) stop the API before it touches the database.
    app.Services.GetRequiredService<IStartupValidator>().Validate();

    // Migrations run here in Development, or when Database:ApplyMigrationsOnStartup is set. Never in Production, whatever the setting
    // says: the API connects there as a user without DDL rights, and T06 applies migrations in a step before the API starts.
    if (!app.Environment.IsProduction()
        && (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup")))
    {
        await app.Services.MigrateDatabaseAsync();
    }

    await app.Services.SeedAdminAccountAsync();

    // T05: `dotnet run -- seed-demo` creates the demo data and exits, without starting the server (so no email is ever sent).
    if (DemoDataCommand.IsRequested(args))
    {
        return await DemoDataCommand.RunAsync(app.Services, Console.Out, Console.Error, CancellationToken.None);
    }

    // US04: `-- set-admin-password` changes the admin's password (read from ADMIN_NEW_PASSWORD) and exits, without starting the server.
    if (AdminPasswordCommand.IsRequested(args))
    {
        return await AdminPasswordCommand.RunAsync(app.Services, args, Environment.GetEnvironmentVariable, Console.Out, Console.Error, CancellationToken.None);
    }
}

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

await app.RunAsync();
return 0;
