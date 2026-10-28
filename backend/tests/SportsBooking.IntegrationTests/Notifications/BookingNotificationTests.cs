using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Npgsql;
using SportsBooking.Application.Notifications;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Identity;
using SportsBooking.Infrastructure.Notifications;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Notifications;

/// <summary>
/// US23 (the owner is told about a new booking) and US21 (the player is reminded 3 hours before). Each test has its own database,
/// the fake email sender and a fake clock; the worker is driven by the test. Sessions are made at the real time and the clock is set
/// afterwards, because an access token is checked against the system clock.
/// </summary>
public sealed class BookingNotificationTests(ApiFactory factory)
{
    // Friday 1 May 2026, 13:00 in Lisbon (WEST). Monday 4 May is open 09:00-13:00 (TestVenues.MondayMorning): slots at 08, 09, 10 and 11 UTC.
    private const string Friday = "2026-05-01T12:00:00Z";
    private const string Slot = "2026-05-04T09:00:00Z";

    private static readonly string[] MondayOnly = ["monday"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly SemaphoreSlim Hosts = new(4);

    private sealed record Site(WebApplicationFactory<Program> App, FakeEmailSender Sender, SettableTimeProvider Clock, string Database, string ConnectionString) : IAsyncDisposable
    {
        public async Task<int> RunWorkerAsync()
        {
            await using var scope = App.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessDueAsync(Ct);
        }

        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            await App.DisposeAsync();
            NpgsqlConnection.ClearPool(new NpgsqlConnection(ConnectionString));
            Hosts.Release();
        }
    }

    private Site Start(string? database = null, string at = Friday)
    {
        Hosts.Wait(Ct);
        try
        {
            database ??= $"us21_{Guid.NewGuid():N}";
            var connectionString = factory.ConnectionStringFor(database) + ";Maximum Pool Size=6;Minimum Pool Size=0";
            var sender = new FakeEmailSender();
            // Sessions are made first, at the real time; the tests then move the clock where they need it.
            var clock = new SettableTimeProvider(DateTimeOffset.UtcNow);
            var app = factory.CreateIsolatedApp(database, new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connectionString })
                .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IEmailSender>(sender);
                    services.AddSingleton<TimeProvider>(clock);
                }));
            _ = app.Server;
            clock.SetUtcNow(DateTimeOffset.Parse(at, CultureInfo.InvariantCulture));
            return new Site(app, sender, clock, database, connectionString);
        }
        catch
        {
            Hosts.Release();
            throw;
        }
    }

    private sealed record Scenario(TestClub Club, TestPitch Pitch, AppUser Owner, string OwnerToken, AppUser Player, string PlayerToken);

    /// <summary>An owner with a club and a pitch (Monday mornings), and a player, both signed in. Sessions are made before the clock moves.</summary>
    private static async Task<Scenario> ScenarioAsync(Site site, string playerName = "Rita Jogadora", string ownerName = "Dona Clube")
    {
        // The sessions are made with the host's clock at the real time (see Start), so create them first.
        site.Clock.SetUtcNow(DateTimeOffset.UtcNow);
        var (owner, ownerTokens) = await site.App.CreateSignedInAsync(Profiles.Owner, ownerName);
        var (player, playerTokens) = await site.App.CreateSignedInAsync(Profiles.Player, playerName);
        var club = await site.App.CreateClubAsync(owner.Id, name: "Clube da Foz");
        var pitch = await site.App.CreatePitchAsync(club.Id, "Padel 1");
        site.Clock.SetUtcNow(DateTimeOffset.Parse(Friday, CultureInfo.InvariantCulture));
        return new Scenario(club, pitch, owner, ownerTokens.AccessToken, player, playerTokens.AccessToken);
    }

    private static async Task<HttpResponseMessage> SendAsync(Site site, HttpMethod method, string path, string token, object? body = null)
    {
        using var client = site.App.CreateSiteClient();
        using var request = new HttpRequestMessage(method, path).WithCookies((SessionCookies.AccessToken, token));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request, Ct);
    }

    private static async Task<Guid> BookAsync(Site site, Scenario scenario, string startsAt = Slot)
    {
        using var response = await SendAsync(site, HttpMethod.Post, "/api/bookings", scenario.PlayerToken, new { pitchId = scenario.Pitch.Id, startsAt });
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, text);
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<List<OutboxMessage>> OutboxAsync(Site site, string type)
    {
        await using var scope = site.App.Services.CreateAsyncScope();
        var all = await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.AsNoTracking().OrderBy(message => message.CreatedAt).ToListAsync(Ct);
        return all.Where(message => message.Type == type).ToList();
    }

    private static List<EmailMessage> SentTo(Site site, string address) => site.Sender.Sent.Where(sent => sent.ToAddress == address).ToList();

    // ---- US23: the owner is told

    [Fact]
    public async Task A_players_booking_queues_an_email_to_the_owner_with_only_the_booking_id()
    {
        await using var site = Start();
        var scenario = await ScenarioAsync(site);

        var bookingId = await BookAsync(site, scenario);

        var queued = (await OutboxAsync(site, OutboxMessageTypes.OwnerNewBooking)).ShouldHaveSingleItem();
        BookingRefPayload.Deserialize(queued.Payload).BookingId.ShouldBe(bookingId);
        using var payload = JsonDocument.Parse(queued.Payload);
        payload.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["bookingId"]);
        queued.NextAttemptAt.ShouldBe(queued.CreatedAt);
    }

    [Fact]
    public async Task The_email_names_club_pitch_date_time_in_lisbon_and_the_player_and_goes_to_the_owners_current_address()
    {
        await using var site = Start();
        var scenario = await ScenarioAsync(site, playerName: "Rita Jogadora", ownerName: "Dona Clube");
        await BookAsync(site, scenario);
        // The owner changes their address before the email is sent: the current one is used.
        var newAddress = $"novo-{Guid.NewGuid():N}@exemplo.test";
        await using (var scope = site.App.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var owner = (await users.FindByIdAsync(scenario.Owner.Id.ToString()))!;
            (await users.SetEmailAsync(owner, newAddress)).Succeeded.ShouldBeTrue();
        }

        await site.RunWorkerAsync();

        var email = SentTo(site, newAddress).ShouldHaveSingleItem();
        SentTo(site, scenario.Owner.Email!).ShouldBeEmpty();
        email.Subject.ShouldBe("Nova reserva: Clube da Foz, 04/05/2026 às 10:00");
        email.TextBody.ShouldContain("Olá Dona Clube,");
        email.TextBody.ShouldContain("Clube: Clube da Foz");
        email.TextBody.ShouldContain("Campo: Padel 1");
        email.TextBody.ShouldContain("Data: segunda-feira, 4 de maio de 2026");
        email.TextBody.ShouldContain("Hora: 10:00 – 11:00");
        email.TextBody.ShouldContain("Jogador: Rita Jogadora");
        // Nothing else about the player.
        email.TextBody.ShouldNotContain(scenario.Player.Email!);
        email.HtmlBody.ShouldNotContain(scenario.Player.Email!);
        email.HtmlBody.ShouldContain("lang=\"pt-PT\"");
    }

    [Fact]
    public async Task A_club_with_the_setting_off_sends_no_owner_email_and_the_setting_is_edited_in_the_club_update()
    {
        await using var site = Start();
        var scenario = await ScenarioAsync(site);
        var path = $"/api/manage/clubs/{scenario.Club.Id}";
        var update = new
        {
            name = "Clube da Foz",
            address = "Rua Principal 1",
            locality = "Porto",
            phone = "912345678",
            contactEmail = "geral@clube.test",
            cancellationWindowHours = 24,
        };

        // The setting is on for a club that never said: an update that does not mention it leaves it on.
        using (var created = await SendAsync(site, HttpMethod.Get, path, scenario.OwnerToken))
        {
            (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("notifyOnNewBooking").GetBoolean().ShouldBeTrue();
        }

        using (var untouched = await SendAsync(site, HttpMethod.Put, path, scenario.OwnerToken, update))
        {
            (await untouched.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("notifyOnNewBooking").GetBoolean().ShouldBeTrue();
        }

        using (var off = await SendAsync(site, HttpMethod.Put, path, scenario.OwnerToken, new { update.name, update.address, update.locality, update.phone, update.contactEmail, update.cancellationWindowHours, notifyOnNewBooking = false }))
        {
            off.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await off.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("notifyOnNewBooking").GetBoolean().ShouldBeFalse();
        }

        // An update that does not know the setting does not switch it back on.
        using (var again = await SendAsync(site, HttpMethod.Put, path, scenario.OwnerToken, update))
        {
            (await again.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("notifyOnNewBooking").GetBoolean().ShouldBeFalse();
        }

        await BookAsync(site, scenario);

        (await OutboxAsync(site, OutboxMessageTypes.OwnerNewBooking)).ShouldBeEmpty();
        // The player still gets their own emails.
        (await OutboxAsync(site, OutboxMessageTypes.BookingConfirmed)).ShouldHaveSingleItem();
        (await OutboxAsync(site, OutboxMessageTypes.BookingReminder)).ShouldHaveSingleItem();

        using (var on = await SendAsync(site, HttpMethod.Put, path, scenario.OwnerToken, new { update.name, update.address, update.locality, update.phone, update.contactEmail, update.cancellationWindowHours, notifyOnNewBooking = true }))
        {
            (await on.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("notifyOnNewBooking").GetBoolean().ShouldBeTrue();
        }

        await BookAsync(site, scenario, "2026-05-04T10:00:00Z");
        (await OutboxAsync(site, OutboxMessageTypes.OwnerNewBooking)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_new_club_has_the_setting_on_and_can_be_created_with_it_off()
    {
        await using var site = Start();
        var scenario = await ScenarioAsync(site);
        var body = new { name = "Clube Novo", address = "Rua 1", locality = "Porto", phone = "912345678", contactEmail = "geral@clube.test" };

        using var onByDefault = await SendAsync(site, HttpMethod.Post, "/api/manage/clubs", scenario.OwnerToken, body);
        using var off = await SendAsync(site, HttpMethod.Post, "/api/manage/clubs", scenario.OwnerToken, new { body.name, body.address, body.locality, body.phone, body.contactEmail, notifyOnNewBooking = false });

        onByDefault.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await onByDefault.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("notifyOnNewBooking").GetBoolean().ShouldBeTrue();
        (await off.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("notifyOnNewBooking").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task What_the_owner_books_for_a_customer_sends_nothing_to_anyone()
    {
        await using var site = Start();
        var scenario = await ScenarioAsync(site);

        using var response = await SendAsync(site, HttpMethod.Post, $"/api/manage/clubs/{scenario.Club.Id}/bookings", scenario.OwnerToken,
            new { pitchId = scenario.Pitch.Id, startsAt = Slot, customerName = "Cliente da Rua", customerPhone = "912345678" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        await using var scope = site.App.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task A_booking_that_loses_the_race_leaves_no_email_and_no_reminder_behind()
    {
        await using var site = Start();
        var scenario = await ScenarioAsync(site);
        await BookAsync(site, scenario);
        // A session is made at the real time (its token is checked against the system clock), then the clock goes back to the test's date.
        var testClock = site.Clock.GetUtcNow();
        site.Clock.SetUtcNow(DateTimeOffset.UtcNow);
        var secondToken = (await site.App.CreateSignedInAsync(Profiles.Player, "Rui Jogador")).Tokens.AccessToken;
        site.Clock.SetUtcNow(testClock);

        using var refused = await SendAsync(site, HttpMethod.Post, "/api/bookings", secondToken, new { pitchId = scenario.Pitch.Id, startsAt = Slot });

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await OutboxAsync(site, OutboxMessageTypes.OwnerNewBooking)).ShouldHaveSingleItem();
        (await OutboxAsync(site, OutboxMessageTypes.BookingReminder)).ShouldHaveSingleItem();
        (await OutboxAsync(site, OutboxMessageTypes.BookingConfirmed)).ShouldHaveSingleItem();
    }

    // ---- US21: the reminder is queued

    [Fact]
    public async Task The_reminder_is_queued_with_the_booking_and_due_three_hours_before_the_start()
    {
        await using var site = Start();
        var scenario = await ScenarioAsync(site);

        var bookingId = await BookAsync(site, scenario);

        var reminder = (await OutboxAsync(site, OutboxMessageTypes.BookingReminder)).ShouldHaveSingleItem();
        BookingRefPayload.Deserialize(reminder.Payload).BookingId.ShouldBe(bookingId);
        reminder.NextAttemptAt.ShouldBe(TestVenues.Utc("2026-05-04T06:00:00Z"));
        reminder.CreatedAt.ShouldBe(TestVenues.Utc(Friday));
        reminder.IsPending.ShouldBeTrue();
        // The confirmation, in contrast, is due at once.
        (await OutboxAsync(site, OutboxMessageTypes.BookingConfirmed)).Single().NextAttemptAt.ShouldBe(TestVenues.Utc(Friday));
    }

    [Theory]
    [InlineData("2026-05-04T08:30:00Z", false)] // 1 h 30 min ahead
    [InlineData("2026-05-04T07:30:00Z", false)] // 2 h 30 min ahead
    [InlineData("2026-05-04T07:00:00Z", false)] // exactly 3 hours ahead: provisional, none
    [InlineData("2026-05-04T06:59:59Z", true)] // one second more than 3 hours
    [InlineData("2026-05-03T09:00:00Z", true)] // the day before
    public async Task A_booking_made_less_than_three_hours_ahead_gets_no_reminder(string now, bool expected)
    {
        await using var site = Start();
        var scenario = await ScenarioAsync(site);
        // The booking starts at 10:00 UTC on Monday (11:00 in Lisbon): the slot is open, and "ahead" counts from the clock set here.
        site.Clock.SetUtcNow(TestVenues.Utc(now));

        await BookAsync(site, scenario, "2026-05-04T10:00:00Z");

        (await OutboxAsync(site, OutboxMessageTypes.BookingReminder)).Count.ShouldBe(expected ? 1 : 0);
        // Everything else is queued as usual.
        (await OutboxAsync(site, OutboxMessageTypes.BookingConfirmed)).ShouldHaveSingleItem();
        (await OutboxAsync(site, OutboxMessageTypes.OwnerNewBooking)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_customers_booking_gets_no_reminder()
    {
        await using var site = Start();
        var scenario = await ScenarioAsync(site);

        using var response = await SendAsync(site, HttpMethod.Post, $"/api/manage/clubs/{scenario.Club.Id}/bookings", scenario.OwnerToken,
            new { pitchId = scenario.Pitch.Id, startsAt = Slot, customerName = "Cliente da Rua", customerPhone = "912345678" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await OutboxAsync(site, OutboxMessageTypes.BookingReminder)).ShouldBeEmpty();
    }

    // ---- US10: the confirmation shows the price the booking stored

    [Fact]
    public async Task The_confirmation_email_shows_the_band_price_stored_in_the_booking_even_if_the_band_changes_before_it_is_sent()
    {
        await using var site = Start();
        var scenario = await ScenarioAsync(site);
        var bandId = await site.App.AddPriceBandAsync(scenario.Pitch.Id, 35.5m, "10:00", "12:00", DayOfWeek.Monday); // Slot is 10:00 in Lisbon
        var bookingId = await BookAsync(site, scenario);

        using var change = await SendAsync(
            site,
            HttpMethod.Put,
            $"/api/manage/clubs/{scenario.Club.Id}/pitches/{scenario.Pitch.Id}/price-bands/{bandId}",
            scenario.OwnerToken,
            new { days = MondayOnly, start = "10:00", end = "12:00", price = 50m });
        change.StatusCode.ShouldBe(HttpStatusCode.OK);
        await site.RunWorkerAsync();

        var email = SentTo(site, scenario.Player.Email!).Single(sent => sent.Subject.StartsWith("Reserva confirmada", StringComparison.Ordinal));
        email.TextBody.ShouldContain("Preço: 35,50 €");
        email.TextBody.ShouldNotContain("50,00 €");
        await using var scope = site.App.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.AsNoTracking().SingleAsync(booking => booking.Id == bookingId, Ct)).Price.ShouldBe(35.5m);
    }

    // ---- US20: one summary per weekly series, owner emails and reminders per booking

    [Fact]
    public async Task A_weekly_series_sends_the_player_one_summary_and_the_owner_one_email_and_a_reminder_per_booking()
    {
        await using var site = Start();
        var scenario = await ScenarioAsync(site, playerName: "Rita Jogadora");
        // 11 May is taken by somebody else: it is left out, and the summary says so.
        await site.App.AddBookingAsync(scenario.Pitch.Id, TestVenues.Utc("2026-05-11T09:00:00Z"), TestVenues.Utc("2026-05-11T10:00:00Z"));
        var remindersBefore = (await OutboxAsync(site, OutboxMessageTypes.BookingReminder)).Count;

        using var response = await SendAsync(site, HttpMethod.Post, "/api/bookings/recurring", scenario.PlayerToken, new { pitchId = scenario.Pitch.Id, startsAt = Slot, weeks = 3 });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));

        (await OutboxAsync(site, OutboxMessageTypes.BookingSeriesConfirmed)).Count.ShouldBe(1);
        (await OutboxAsync(site, OutboxMessageTypes.BookingConfirmed)).ShouldBeEmpty();
        (await OutboxAsync(site, OutboxMessageTypes.OwnerNewBooking)).Count.ShouldBe(2);
        ((await OutboxAsync(site, OutboxMessageTypes.BookingReminder)).Count - remindersBefore).ShouldBe(2);

        await site.RunWorkerAsync();

        var toPlayer = SentTo(site, scenario.Player.Email!);
        var summary = toPlayer.ShouldHaveSingleItem(); // the reminders are not due yet
        summary.Subject.ShouldBe("Reservas semanais confirmadas: Clube da Foz, 2 datas");
        summary.TextBody.ShouldContain("Olá Rita Jogadora,");
        summary.TextBody.ShouldContain("- segunda-feira, 4 de maio de 2026, 10:00 – 11:00 · 20,00 €");
        summary.TextBody.ShouldContain("- segunda-feira, 18 de maio de 2026, 10:00 – 11:00 · 20,00 €");
        summary.TextBody.ShouldContain("Estas datas ficaram de fora e não foram reservadas:");
        summary.TextBody.ShouldContain("- segunda-feira, 11 de maio de 2026, 10:00 – 11:00: já está reservado");
        summary.TextBody.ShouldContain("Campo: Padel 1");
        summary.HtmlBody.ShouldContain("<li>segunda-feira, 4 de maio de 2026, 10:00 – 11:00 · 20,00 €</li>");
        SentTo(site, scenario.Owner.Email!).Count.ShouldBe(2);
        site.Sender.Sent.ShouldNotContain(sent => sent.Subject.StartsWith("Reserva confirmada", StringComparison.Ordinal));
    }

    // ---- US21: the reminder is sent, or skipped

    [Fact]
    public async Task The_reminder_is_sent_when_due_and_not_before_with_club_address_pitch_date_and_time()
    {
        await using var site = Start();
        var scenario = await ScenarioAsync(site, playerName: "Rita Jogadora");
        await BookAsync(site, scenario);
        await site.RunWorkerAsync(); // the confirmation and the owner's email, due now
        site.Sender.Sent.Select(sent => sent.Subject).ShouldNotContain(subject => subject.StartsWith("Lembrete", StringComparison.Ordinal));

        site.Clock.SetUtcNow(TestVenues.Utc("2026-05-04T05:59:59Z"));
        (await site.RunWorkerAsync()).ShouldBe(0);
        site.Clock.SetUtcNow(TestVenues.Utc("2026-05-04T06:00:00Z"));
        (await site.RunWorkerAsync()).ShouldBe(1);

        var reminder = SentTo(site, scenario.Player.Email!).Single(sent => sent.Subject.StartsWith("Lembrete", StringComparison.Ordinal));
        reminder.Subject.ShouldBe("Lembrete: a tua reserva começa às 10:00, Clube da Foz");
        reminder.TextBody.ShouldContain("Olá Rita Jogadora,");
        reminder.TextBody.ShouldContain("começa dentro de 3 horas");
        reminder.TextBody.ShouldContain("Clube: Clube da Foz");
        reminder.TextBody.ShouldContain("Morada: Rua Principal 1, Porto");
        reminder.TextBody.ShouldContain("Campo: Padel 1");
        reminder.TextBody.ShouldContain("Data: segunda-feira, 4 de maio de 2026");
        reminder.TextBody.ShouldContain("Hora: 10:00 – 11:00");
        (await OutboxAsync(site, OutboxMessageTypes.BookingReminder)).Single().SentAt.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("player")]
    [InlineData("club")]
    public async Task A_reminder_for_a_booking_cancelled_before_it_is_due_is_skipped_not_sent_and_not_failed(string cancelledBy)
    {
        await using var site = Start();
        var scenario = await ScenarioAsync(site);
        var bookingId = await BookAsync(site, scenario);
        await site.RunWorkerAsync();
        var sentBefore = site.Sender.Sent.Count;

        using (var cancelled = cancelledBy == "player"
            ? await SendAsync(site, HttpMethod.Post, $"/api/bookings/{bookingId}/cancel", scenario.PlayerToken)
            : await SendAsync(site, HttpMethod.Post, $"/api/manage/clubs/{scenario.Club.Id}/bookings/{bookingId}/cancel", scenario.OwnerToken, new { reason = "Campo em obras" }))
        {
            cancelled.StatusCode.ShouldBe(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync(Ct));
        }

        site.Clock.SetUtcNow(TestVenues.Utc("2026-05-04T06:00:00Z"));
        await site.RunWorkerAsync();

        var reminder = (await OutboxAsync(site, OutboxMessageTypes.BookingReminder)).Single();
        reminder.SkippedAt.ShouldNotBeNull();
        reminder.SentAt.ShouldBeNull();
        reminder.FailedAt.ShouldBeNull();
        reminder.LastError.ShouldBe("booking_not_confirmed");
        reminder.IsPending.ShouldBeFalse();
        // The cancellation email of US22 was sent, the reminder was not.
        site.Sender.Sent.Skip(sentBefore).Select(sent => sent.Subject).ShouldNotContain(subject => subject.StartsWith("Lembrete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_reminder_that_is_due_after_the_start_because_the_worker_was_down_is_skipped()
    {
        await using var site = Start();
        var scenario = await ScenarioAsync(site);
        await BookAsync(site, scenario);

        site.Clock.SetUtcNow(TestVenues.Utc("2026-05-04T09:30:00Z"));
        await site.RunWorkerAsync();

        var reminder = (await OutboxAsync(site, OutboxMessageTypes.BookingReminder)).Single();
        reminder.SkippedAt.ShouldNotBeNull();
        reminder.LastError.ShouldBe("booking_started");
        SentTo(site, scenario.Player.Email!).Select(sent => sent.Subject).ShouldNotContain(subject => subject.StartsWith("Lembrete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_reminder_is_never_sent_twice_not_by_a_second_pass_a_restart_or_a_second_row()
    {
        await using var site = Start();
        var scenario = await ScenarioAsync(site);
        var bookingId = await BookAsync(site, scenario);
        site.Clock.SetUtcNow(TestVenues.Utc("2026-05-04T06:00:00Z"));
        await site.RunWorkerAsync();
        var reminders = () => SentTo(site, scenario.Player.Email!).Count(sent => sent.Subject.StartsWith("Lembrete", StringComparison.Ordinal));
        reminders().ShouldBe(1);

        // A second pass finds nothing to do.
        site.Clock.SetUtcNow(TestVenues.Utc("2026-05-04T06:30:00Z"));
        (await site.RunWorkerAsync()).ShouldBe(0);
        reminders().ShouldBe(1);

        // The database refuses a second reminder, or a second owner's email, for the same booking.
        foreach (var type in new[] { OutboxMessageTypes.BookingReminder, OutboxMessageTypes.OwnerNewBooking })
        {
            await using var scope = site.App.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.OutboxMessages.Add(OutboxMessage.Create(type, new BookingRefPayload(bookingId).Serialize(), TestVenues.Utc(Friday)));
            var failure = await Should.ThrowAsync<DbUpdateException>(async () => await dbContext.SaveChangesAsync(Ct), type);
            failure.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        }

        // A new host on the same database (the API restarted) sends nothing again.
        var database = site.Database;
        await site.DisposeAsync();
        await using var restarted = Start(database);
        restarted.Clock.SetUtcNow(TestVenues.Utc("2026-05-04T07:00:00Z"));
        (await restarted.RunWorkerAsync()).ShouldBe(0);
        restarted.Sender.Sent.ShouldBeEmpty();
    }

    // ---- The clock change of 25 October 2026

    [Fact]
    public async Task A_reminder_for_a_booking_just_after_the_clock_change_is_due_at_the_right_utc_instant()
    {
        // Sunday 25 October 2026: at 02:00 summer time (01:00 UTC) the clocks go back to 01:00. The pitch is open from midnight, so
        // there are slots at 23:00 UTC (00:00 summer), 00:00 UTC (01:00 summer), 01:00 UTC (01:00 winter, the repeated hour) and so on.
        await using var site = Start(at: "2026-10-24T12:00:00Z");
        var scenario = await ScenarioAsync(site);
        var pitch = await site.App.CreatePitchAsync(scenario.Club.Id, "Padel 2", slotMinutes: 60, open: (DayOfWeek.Sunday, "00:00", "06:00"));
        site.Clock.SetUtcNow(TestVenues.Utc("2026-10-24T12:00:00Z"));

        // The second 01:00 of the night, the first hour after the change.
        using var response = await SendAsync(site, HttpMethod.Post, "/api/bookings", scenario.PlayerToken, new { pitchId = pitch.Id, startsAt = "2026-10-25T01:00:00Z" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));

        // Three hours of elapsed time before 01:00 UTC is 22:00 UTC (23:00 summer time), not 21:00 UTC, which is what taking three
        // hours off the local 01:00 would give.
        var reminder = (await OutboxAsync(site, OutboxMessageTypes.BookingReminder)).Single();
        reminder.NextAttemptAt.ShouldBe(TestVenues.Utc("2026-10-24T22:00:00Z"));

        site.Clock.SetUtcNow(TestVenues.Utc("2026-10-24T21:59:59Z"));
        (await site.RunWorkerAsync()).ShouldBeGreaterThan(0); // the confirmation and the owner's email, due at once
        SentTo(site, scenario.Player.Email!).Select(sent => sent.Subject).ShouldNotContain(subject => subject.StartsWith("Lembrete", StringComparison.Ordinal));
        site.Clock.SetUtcNow(TestVenues.Utc("2026-10-24T22:00:00Z"));
        (await site.RunWorkerAsync()).ShouldBe(1);

        var email = SentTo(site, scenario.Player.Email!).Single(sent => sent.Subject.StartsWith("Lembrete", StringComparison.Ordinal));
        // The repeated hour is told apart, as on the pages.
        email.Subject.ShouldBe("Lembrete: a tua reserva começa às 01:00 (inverno), Clube da Foz");
        email.TextBody.ShouldContain("Hora: 01:00 (inverno) – 02:00");
        email.TextBody.ShouldContain("Data: domingo, 25 de outubro de 2026");
    }

    [Fact]
    public async Task The_owner_email_is_skipped_when_the_owner_was_deactivated_before_it_is_sent()
    {
        await using var site = Start();
        var scenario = await ScenarioAsync(site);
        await BookAsync(site, scenario);
        await site.App.DeactivateAccountAsync(scenario.Owner.Id);

        await site.RunWorkerAsync();

        var ownerEmail = (await OutboxAsync(site, OutboxMessageTypes.OwnerNewBooking)).Single();
        ownerEmail.SkippedAt.ShouldNotBeNull();
        ownerEmail.SentAt.ShouldBeNull();
        ownerEmail.FailedAt.ShouldBeNull();
        ownerEmail.LastError.ShouldBe("owner_deactivated");
        SentTo(site, scenario.Owner.Email!).ShouldBeEmpty();
        // The player's own emails are not affected.
        SentTo(site, scenario.Player.Email!).ShouldHaveSingleItem().Subject.ShouldStartWith("Reserva confirmada");
        var logs = string.Join(Environment.NewLine, site.App.Services.GetFakeLogCollector().GetSnapshot().Select(record => $"{record.Message} {record.Exception}"));
        logs.ShouldNotContain(scenario.Owner.Email!, Case.Insensitive);
        logs.ShouldContain("skipped: owner_deactivated");
    }

    [Fact]
    public async Task The_owner_email_of_an_active_owner_is_still_sent()
    {
        await using var site = Start();
        var scenario = await ScenarioAsync(site);
        await BookAsync(site, scenario);

        await site.RunWorkerAsync();

        SentTo(site, scenario.Owner.Email!).ShouldHaveSingleItem().Subject.ShouldStartWith("Nova reserva");
        (await OutboxAsync(site, OutboxMessageTypes.OwnerNewBooking)).Single().SkippedAt.ShouldBeNull();
    }

    // ---- Logs

    [Fact]
    public async Task No_address_or_body_reaches_the_logs()
    {
        await using var site = Start();
        var scenario = await ScenarioAsync(site, playerName: "Rita Jogadora");
        await BookAsync(site, scenario);
        await site.RunWorkerAsync();
        site.Clock.SetUtcNow(TestVenues.Utc("2026-05-04T06:00:00Z"));
        await site.RunWorkerAsync();

        var logs = string.Join("\n", site.App.Services.GetFakeLogCollector().GetSnapshot().Select(record => $"{record.Message} {record.Exception}"));

        foreach (var secret in new[] { scenario.Player.Email!, scenario.Owner.Email!, "Rita Jogadora", "Rua Principal", "Entrou uma reserva", "Lembrete" })
        {
            logs.ShouldNotContain(secret, Case.Insensitive);
        }
    }
}
