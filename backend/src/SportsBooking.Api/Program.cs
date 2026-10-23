using System.Reflection;
using Microsoft.Extensions.Options;
using SportsBooking.Api.Auth;
using SportsBooking.Api.ErrorHandling;
using SportsBooking.Api.Security;
using SportsBooking.Application;
using SportsBooking.Infrastructure;

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
    ]);
}

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddApiProblemDetails();
builder.Services.AddOriginCheck(builder.Configuration);
builder.Services.AddCookieJwtAuthentication();
builder.Services.AddOpenApi(options => options.AddSchemaTransformer<ProblemDetailsSchemaTransformer>());

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseOriginCheck();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "SportsBooking API v1"));
}

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
}

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

await app.RunAsync();
