using System.Reflection;
using Microsoft.Extensions.Options;
using SportsBooking.Api.Auth;
using SportsBooking.Api.ErrorHandling;
using SportsBooking.Api.Security;
using SportsBooking.Application;
using SportsBooking.Infrastructure;
using SportsBooking.Infrastructure.Demo;

var builder = WebApplication.CreateBuilder(args);

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
    ]);
}

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

// Missing fields reach FluentValidation (pt-PT messages per field) instead of MVC's implicit [Required].
builder.Services.AddControllers(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);
builder.Services.AddApiProblemDetails();
builder.Services.AddOriginCheck(builder.Configuration);
builder.Services.AddAuthRateLimiting(builder.Configuration);
builder.Services.AddCookieJwtAuthentication();
builder.Services.AddOpenApi(options => options.AddSchemaTransformer<ProblemDetailsSchemaTransformer>());

var app = builder.Build();

// First: the client IP (rate limiting) comes from X-Forwarded-For only when a trusted proxy sent it.
app.UseForwardedHeaders();
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

    // Production applies migrations with a migration bundle at deploy time, before the API starts.
    if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
    {
        await app.Services.MigrateDatabaseAsync();
    }

    await app.Services.SeedAdminAccountAsync();

    // T05: `dotnet run -- seed-demo` creates the demo data and exits, without starting the server (so no email is ever sent).
    if (DemoDataCommand.IsRequested(args))
    {
        return await DemoDataCommand.RunAsync(app.Services, Console.Out, Console.Error, CancellationToken.None);
    }
}

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

await app.RunAsync();
return 0;
