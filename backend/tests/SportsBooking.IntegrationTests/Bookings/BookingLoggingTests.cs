using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Bookings;

/// <summary>
/// T03 logging: a lost booking race is expected and logged at Information by the store; EF's own SaveChangesFailed
/// event is Debug. A database failure nobody handles still ends in the global exception handler, logged at Error.
/// </summary>
public sealed class BookingLoggingTests(ApiFactory factory)
{
    private const string BookingStoreCategory = "SportsBooking.Infrastructure.Bookings.BookingStore";
    private const string ExceptionHandlerCategory = "SportsBooking.Api.ErrorHandling.GlobalExceptionHandler";
    private const string EfUpdateCategory = "Microsoft.EntityFrameworkCore.Update";
    private const string EfCommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

    // Friday 1 May 2026, 11:00 in Lisbon. Monday 4 May is open 09:00-13:00 (TestVenues.MondayMorning).
    private const string Clock = "2026-05-01T10:00:00Z";
    private const string Slot = "2026-05-04T08:00:00Z";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The API on the shared database with a fixed clock, capturing every log down to Debug for EF's update events.</summary>
    private WebApplicationFactory<Program> LoggedApp() =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse(Clock, CultureInfo.InvariantCulture))));
            builder.ConfigureLogging(logging =>
            {
                logging.AddFakeLogging();
                logging.AddFilter(EfUpdateCategory, LogLevel.Debug);
                logging.AddFilter(EfCommandCategory, LogLevel.Debug);
            });
        });

    private static async Task<HttpResponseMessage> BookAsync(WebApplicationFactory<Program> app, string accessToken, Guid pitchId)
    {
        using var client = app.CreateSiteClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/bookings") { Content = JsonContent.Create(new { pitchId, startsAt = Slot }) };
        request.WithCookies((SessionCookies.AccessToken, accessToken));
        return await client.SendAsync(request, Ct);
    }

    [Fact]
    public async Task A_lost_booking_race_is_logged_at_information_without_the_player_and_never_as_an_error()
    {
        await using var app = LoggedApp();
        var club = await factory.CreateClubAsync();
        var pitch = await factory.CreatePitchAsync(club.Id);
        var winner = await factory.CreateSignedInPlayerAsync();
        var loser = await factory.CreateSignedInPlayerAsync();
        (await BookAsync(app, winner.Tokens.AccessToken, pitch.Id)).StatusCode.ShouldBe(HttpStatusCode.Created);

        using var response = await BookAsync(app, loser.Tokens.AccessToken, pitch.Id);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var logs = app.Services.GetFakeLogCollector().GetSnapshot();

        var conflict = logs.Where(record => record.Category == BookingStoreCategory).ShouldHaveSingleItem();
        conflict.Level.ShouldBe(LogLevel.Information);
        conflict.Message.ShouldContain(pitch.Id.ToString());
        conflict.Message.ShouldContain("2026-05-04T08:00:00");
        // No personal data: neither player id nor email, in the text or in the structured values.
        var everythingLogged = conflict.Message + string.Join(' ', conflict.StructuredState!.Select(pair => pair.Value));
        everythingLogged.ShouldNotContain(loser.User.Id.ToString());
        everythingLogged.ShouldNotContain(winner.User.Id.ToString());
        everythingLogged.ShouldNotContain(loser.User.Email!);

        // EF's two events for the failure still exist, but at Debug: a conflict the store handles is not an error.
        logs.Where(record => record.Id == CoreEventId.SaveChangesFailed).ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Debug);
        logs.Where(record => record.Id == RelationalEventId.CommandError).ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Debug);
        logs.Where(record => record.Category is EfUpdateCategory or EfCommandCategory).ShouldAllBe(record => record.Level < LogLevel.Warning);
        // A lost race is a normal 409: the exception handler never sees it.
        logs.Where(record => record.Category == ExceptionHandlerCategory).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unexpected_database_error_reaches_the_exception_handler_and_is_logged_at_error_as_a_500()
    {
        await using var app = LoggedApp();
        using var client = app.CreateClient();

        using var response = await client.GetAsync(DatabaseFailureTestController.Route, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe("internal_error");

        var logs = app.Services.GetFakeLogCollector().GetSnapshot();
        var handled = logs.Where(record => record.Category == ExceptionHandlerCategory).ShouldHaveSingleItem();
        handled.Level.ShouldBe(LogLevel.Error);
        var failure = handled.Exception.ShouldBeOfType<Microsoft.EntityFrameworkCore.DbUpdateException>();
        failure.InnerException.ShouldNotBeNull().ShouldBeOfType<Npgsql.PostgresException>().SqlState.ShouldBe(Npgsql.PostgresErrorCodes.ForeignKeyViolation);
        // Nothing went quiet: EF's own events are Debug, but the failed command is still logged at Error (same category
        // as EF's, with the exception) and so is the unhandled exception.
        logs.Where(record => record.Id == CoreEventId.SaveChangesFailed).ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Debug);
        var failedCommand = logs.Where(record => record.Category == EfCommandCategory && record.Level == LogLevel.Error).ShouldHaveSingleItem();
        failedCommand.Message.ShouldContain("Failed executing DbCommand");
        failedCommand.Exception.ShouldBeOfType<Npgsql.PostgresException>().SqlState.ShouldBe(Npgsql.PostgresErrorCodes.ForeignKeyViolation);
        logs.Where(record => record.Category == BookingStoreCategory).ShouldBeEmpty();
    }
}
