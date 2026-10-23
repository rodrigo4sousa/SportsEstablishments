using System.Reflection;
using Microsoft.Extensions.Options;
using SportsBooking.Api.ErrorHandling;
using SportsBooking.Application;
using SportsBooking.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// The build-time OpenAPI generator starts the app without real configuration or a database.
var isOpenApiGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
if (isOpenApiGeneration)
{
    builder.Configuration.AddInMemoryCollection([new("ConnectionStrings:Default", "Host=unused-during-openapi-generation")]);
}

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddApiProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
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
app.MapHealthChecks("/health");

await app.RunAsync();
