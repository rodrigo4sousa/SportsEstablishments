using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using SportsBooking.Application.Auth;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Bookings;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Admin;

/// <summary>
/// US29: the admin deactivates and reactivates an owner, with a reason that is recorded. The tests share the assembly's database
/// (every owner, club and locality is new) and a host whose clock is Wednesday 28 October 2026, 10:00 in Lisbon (UTC+0 since the
/// clocks went back); sessions come from the shared app, because a token is checked against the system clock.
/// </summary>
public sealed class OwnerDeactivationTests(ApiFactory factory)
{
    private const string Password = "palavra-passe-do-dono";
    private const string Now = "2026-10-28T10:00:00Z";

    // Monday 2 November 2026 is open 09:00-13:00 (TestVenues.MondayMorning): one-hour slots from 09:00 UTC.
    private static readonly string[] MondaySlots = ["2026-11-02T09:00:00Z", "2026-11-02T10:00:00Z", "2026-11-02T11:00:00Z", "2026-11-02T12:00:00Z"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> AppAt(string utcNow = Now) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture)))));

    private sealed record TestOwner(Guid Id, string Email, SessionTokens Tokens, TestClub Club, TestPitch Pitch, string Locality);

    /// <summary>An owner who can sign in, with a session, a club with an active pitch (Monday mornings) in a locality of its own.</summary>
    private async Task<TestOwner> OwnerAsync()
    {
        var email = $"dono-{Guid.NewGuid():N}@exemplo.test";
        var account = await factory.CreateAccountAsync(Profiles.Owner, Password, email, "Dona Clube");
        var locality = $"Cidade{Guid.NewGuid():N}";
        var club = await factory.CreateClubAsync(account.Id, locality: locality);
        var pitch = await factory.CreatePitchAsync(club.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var tokens = await scope.ServiceProvider.GetRequiredService<ISessionService>().StartAsync(account.Id, Profiles.Owner, Ct);
        return new TestOwner(account.Id, email, tokens, club, pitch, locality);
    }

    private async Task<string> AdminTokenAsync() => (await factory.CreateSignedInAsync(Profiles.Admin, "Admin do Teste")).Tokens.AccessToken;

    private static async Task<HttpResponseMessage> SendAsync(WebApplicationFactory<Program> app, HttpMethod method, string path, string? accessToken = null, object? body = null, string? refreshToken = null)
    {
        using var client = app.CreateSiteClient();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var cookies = new List<(string, string)>();
        if (accessToken is not null)
        {
            cookies.Add((SessionCookies.AccessToken, accessToken));
        }

        if (refreshToken is not null)
        {
            cookies.Add((SessionCookies.RefreshToken, refreshToken));
        }

        if (cookies.Count > 0)
        {
            request.WithCookies([.. cookies]);
        }

        return await client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(status, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var problem = await JsonAsync(response, status);
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    private static async Task<JsonElement> DeactivateAsync(WebApplicationFactory<Program> app, string admin, Guid ownerId, string reason = "Abuso da plataforma") =>
        await JsonAsync(await SendAsync(app, HttpMethod.Post, $"/api/admin/owners/{ownerId}/deactivate", admin, new { reason }));

    private static async Task<JsonElement> ReactivateAsync(WebApplicationFactory<Program> app, string admin, Guid ownerId, string reason = "Resolvido") =>
        await JsonAsync(await SendAsync(app, HttpMethod.Post, $"/api/admin/owners/{ownerId}/reactivate", admin, new { reason }));

    private static async Task<JsonElement> HistoryAsync(WebApplicationFactory<Program> app, string admin, Guid ownerId) =>
        await JsonAsync(await SendAsync(app, HttpMethod.Get, $"/api/admin/owners/{ownerId}/history", admin));

    private static async Task<HttpResponseMessage> LoginAsync(WebApplicationFactory<Program> app, string email) =>
        await SendAsync(app, HttpMethod.Post, "/api/auth/login", body: new { email, password = Password });

    private static async Task<List<string>> SearchSlugsAsync(WebApplicationFactory<Program> app, string locality)
    {
        using var response = await SendAsync(app, HttpMethod.Get, $"/api/clubs?locality={Uri.EscapeDataString(locality)}");
        var page = await JsonAsync(response);
        return page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("slug").GetString()!).ToList();
    }

    private async Task<List<(string Status, DateTime StartsAt)>> BookingsOfAsync(Guid pitchId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.AsNoTracking()
            .Where(booking => booking.PitchId == pitchId).OrderBy(booking => booking.StartsAt).ToListAsync(Ct);
        return rows.Select(booking => (booking.Status.ToString(), booking.StartsAt)).ToList();
    }

    private async Task<List<(string Action, string Reason, Guid AdminId, DateTime CreatedAt)>> AuditAsync(Guid ownerId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var connection = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetDbConnection();
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT action, reason, admin_id, created_at FROM owner_status_changes WHERE owner_id = $1 ORDER BY created_at, id";
        command.Parameters.Add(new NpgsqlParameter { Value = ownerId });
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<(string, string, Guid, DateTime)>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetGuid(2), reader.GetDateTime(3)));
        }

        return rows;
    }

    private async Task<(int Open, int Revoked)> RefreshTokensAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tokens = await scope.ServiceProvider.GetRequiredService<AppDbContext>().RefreshTokens.AsNoTracking().Where(token => token.UserId == userId).ToListAsync(Ct);
        return (tokens.Count(token => token.RevokedAt is null), tokens.Count(token => token.RevokedAt is not null));
    }

    // ---- The owner, as the admin reads it

    [Fact]
    public async Task An_owner_comes_with_the_row_of_the_list_and_the_number_of_future_confirmed_bookings()
    {
        await using var app = AppAt();
        var owner = await OwnerAsync();
        var admin = await AdminTokenAsync();
        var otherPitch = await factory.CreatePitchAsync(owner.Club.Id, "Campo 2");
        var secondClub = await factory.CreateClubAsync(owner.Id, locality: owner.Locality);
        var thirdPitch = await factory.CreatePitchAsync(secondClub.Id);
        var other = await OwnerAsync();
        var otherOwnersPitch = other.Pitch;

        // Counted: confirmed bookings that have not ended, in any club of the owner, a customer's included.
        await factory.AddBookingAsync(owner.Pitch.Id, TestVenues.Utc("2026-11-02T09:00:00Z"), TestVenues.Utc("2026-11-02T10:00:00Z"));
        await factory.AddCustomerBookingAsync(otherPitch.Id, TestVenues.Utc("2026-11-02T09:00:00Z"), TestVenues.Utc("2026-11-02T10:00:00Z"));
        await factory.AddBookingAsync(thirdPitch.Id, TestVenues.Utc("2026-11-09T09:00:00Z"), TestVenues.Utc("2026-11-09T10:00:00Z"));
        // The one in progress at 10:00 has not ended: counted.
        await factory.AddBookingAsync(owner.Pitch.Id, TestVenues.Utc("2026-10-28T09:30:00Z"), TestVenues.Utc("2026-10-28T10:30:00Z"));
        // Not counted: ended, cancelled, no-show, another owner's.
        await factory.AddBookingAsync(owner.Pitch.Id, TestVenues.Utc("2026-10-28T08:00:00Z"), TestVenues.Utc("2026-10-28T09:00:00Z"));
        var cancelled = await factory.AddBookingAsync(otherPitch.Id, TestVenues.Utc("2026-11-03T09:00:00Z"), TestVenues.Utc("2026-11-03T10:00:00Z"));
        await factory.SetBookingStatusAsync(cancelled.Id, "CancelledByPlayer");
        var noShow = await factory.AddBookingAsync(otherPitch.Id, TestVenues.Utc("2026-11-04T09:00:00Z"), TestVenues.Utc("2026-11-04T10:00:00Z"));
        await factory.SetBookingStatusAsync(noShow.Id, "NoShow");
        await factory.AddBookingAsync(otherOwnersPitch.Id, TestVenues.Utc("2026-11-02T09:00:00Z"), TestVenues.Utc("2026-11-02T10:00:00Z"));

        var detail = await JsonAsync(await SendAsync(app, HttpMethod.Get, $"/api/admin/owners/{owner.Id}", admin));

        detail.GetProperty("id").GetGuid().ShouldBe(owner.Id);
        detail.GetProperty("name").GetString().ShouldBe("Dona Clube");
        detail.GetProperty("email").GetString().ShouldBe(owner.Email);
        detail.GetProperty("deactivated").GetBoolean().ShouldBeFalse();
        detail.GetProperty("clubs").GetInt32().ShouldBe(2);
        detail.GetProperty("pitches").GetInt32().ShouldBe(3);
        detail.GetProperty("futureBookings").GetInt32().ShouldBe(4);
        // The numbers of the list: the not-cancelled bookings of the owner's clubs, and the distinct players.
        detail.GetProperty("bookings").GetInt32().ShouldBe(6);
        detail.GetProperty("players").GetInt32().ShouldBe(5);

        var list = await JsonAsync(await SendAsync(app, HttpMethod.Get, $"/api/admin/owners?search={Uri.EscapeDataString(owner.Email)}", admin));
        var row = list.GetProperty("items").EnumerateArray().Single();
        foreach (var field in new[] { "name", "email", "deactivated", "clubs", "pitches", "bookings", "players" })
        {
            row.GetProperty(field).ToString().ShouldBe(detail.GetProperty(field).ToString(), field);
        }
    }

    [Fact]
    public async Task The_future_bookings_are_still_counted_after_the_owner_is_deactivated()
    {
        await using var app = AppAt();
        var owner = await OwnerAsync();
        var admin = await AdminTokenAsync();
        await factory.AddBookingAsync(owner.Pitch.Id, TestVenues.Utc("2026-11-02T09:00:00Z"), TestVenues.Utc("2026-11-02T10:00:00Z"));

        var after = await DeactivateAsync(app, admin, owner.Id);

        after.GetProperty("deactivated").GetBoolean().ShouldBeTrue();
        after.GetProperty("futureBookings").GetInt32().ShouldBe(1);
    }

    // ---- Deactivating

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_reason_is_required_to_deactivate_and_nothing_changes_without_it(string? reason)
    {
        await using var app = AppAt();
        var owner = await OwnerAsync();
        var admin = await AdminTokenAsync();

        var problem = await JsonAsync(await SendAsync(app, HttpMethod.Post, $"/api/admin/owners/{owner.Id}/deactivate", admin, new { reason }), HttpStatusCode.BadRequest);

        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.GetProperty("errors").GetProperty("reason")[0].GetString().ShouldBe("Indica o motivo.");
        (await AuditAsync(owner.Id)).ShouldBeEmpty();
        (await JsonAsync(await SendAsync(app, HttpMethod.Get, $"/api/admin/owners/{owner.Id}", admin))).GetProperty("deactivated").GetBoolean().ShouldBeFalse();
        (await RefreshTokensAsync(owner.Id)).Revoked.ShouldBe(0);
    }

    [Fact]
    public async Task A_missing_body_is_a_400_too_and_a_reason_of_500_characters_is_the_longest_allowed()
    {
        await using var app = AppAt();
        var owner = await OwnerAsync();
        var admin = await AdminTokenAsync();

        var tooLong = await JsonAsync(await SendAsync(app, HttpMethod.Post, $"/api/admin/owners/{owner.Id}/deactivate", admin, new { reason = new string('x', 501) }), HttpStatusCode.BadRequest);
        tooLong.GetProperty("errors").GetProperty("reason")[0].GetString()!.ShouldContain("500");
        using var empty = await SendAsync(app, HttpMethod.Post, $"/api/admin/owners/{owner.Id}/deactivate", admin, new { });
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // 500 once trimmed.
        await DeactivateAsync(app, admin, owner.Id, "  " + new string('x', 500) + "  ");
        (await AuditAsync(owner.Id)).Single().Reason.ShouldBe(new string('x', 500));
    }

    [Fact]
    public async Task Deactivating_writes_the_audit_row_with_the_trimmed_reason_the_admin_from_the_session_and_the_time()
    {
        await using var app = AppAt();
        var owner = await OwnerAsync();
        var (adminAccount, adminTokens) = await factory.CreateSignedInAsync(Profiles.Admin, "Admin Auditado");

        var result = await DeactivateAsync(app, adminTokens.AccessToken, owner.Id, "  A pedido do dono  ");

        result.GetProperty("deactivated").GetBoolean().ShouldBeTrue();
        var row = (await AuditAsync(owner.Id)).ShouldHaveSingleItem();
        row.Action.ShouldBe("Deactivated");
        row.Reason.ShouldBe("A pedido do dono");
        row.AdminId.ShouldBe(adminAccount.Id);
        row.CreatedAt.ShouldBe(TestVenues.Utc(Now));
    }

    [Fact]
    public async Task Deactivating_revokes_every_refresh_token_so_a_refresh_fails_and_a_new_login_is_refused()
    {
        await using var app = AppAt();
        var owner = await OwnerAsync();
        var admin = await AdminTokenAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // A second session of the same owner.
            await scope.ServiceProvider.GetRequiredService<ISessionService>().StartAsync(owner.Id, Profiles.Owner, Ct);
        }

        (await RefreshTokensAsync(owner.Id)).ShouldBe((2, 0));
        (await LoginAsync(app, owner.Email)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await DeactivateAsync(app, admin, owner.Id);

        (await RefreshTokensAsync(owner.Id)).Open.ShouldBe(0);
        await ProblemAsync(await SendAsync(app, HttpMethod.Post, "/api/auth/refresh", refreshToken: owner.Tokens.RefreshToken), HttpStatusCode.Unauthorized, "invalid_refresh_token");
        await ProblemAsync(await LoginAsync(app, owner.Email), HttpStatusCode.Unauthorized, "invalid_credentials");
    }

    [Fact]
    public async Task The_clubs_leave_the_search_and_the_club_page_and_refuse_bookings_but_existing_future_bookings_stay_confirmed()
    {
        await using var app = AppAt();
        var owner = await OwnerAsync();
        var admin = await AdminTokenAsync();
        var player = await factory.CreateSignedInPlayerAsync();
        var existing = await factory.AddBookingAsync(owner.Pitch.Id, TestVenues.Utc(MondaySlots[0]), TestVenues.Utc(MondaySlots[1]), player.User);

        (await SearchSlugsAsync(app, owner.Locality)).ShouldBe([owner.Club.Slug]);
        (await SendAsync(app, HttpMethod.Get, $"/api/clubs/{owner.Club.Slug}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(app, HttpMethod.Get, $"/api/clubs/{owner.Club.Slug}/pitches/{owner.Pitch.Id}/slots?date=2026-11-02")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await DeactivateAsync(app, admin, owner.Id);

        (await SearchSlugsAsync(app, owner.Locality)).ShouldBeEmpty();
        await ProblemAsync(await SendAsync(app, HttpMethod.Get, $"/api/clubs/{owner.Club.Slug}"), HttpStatusCode.NotFound, "club_not_found");
        await ProblemAsync(await SendAsync(app, HttpMethod.Get, $"/api/clubs/{owner.Club.Slug}/pitches/{owner.Pitch.Id}/slots?date=2026-11-02"), HttpStatusCode.NotFound, "club_not_found");
        await ProblemAsync(
            await SendAsync(app, HttpMethod.Post, "/api/bookings", player.Tokens.AccessToken, new { pitchId = owner.Pitch.Id, startsAt = MondaySlots[2] }),
            HttpStatusCode.UnprocessableEntity,
            "club_unavailable");

        var bookings = await BookingsOfAsync(owner.Pitch.Id);
        bookings.ShouldBe([("Confirmed", TestVenues.Utc(MondaySlots[0]))]);
        existing.Status.ShouldBe(BookingStatus.Confirmed);
        // The player still sees the booking.
        var mine = await JsonAsync(await SendAsync(app, HttpMethod.Get, "/api/me/bookings?scope=upcoming", player.Tokens.AccessToken));
        mine.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ShouldBe([existing.Id]);
    }

    [Fact]
    public async Task Reactivating_restores_signing_in_refreshing_the_search_the_club_page_and_bookings_and_the_history_has_both_changes_newest_first()
    {
        var owner = await OwnerAsync();
        var (adminAccount, adminTokens) = await factory.CreateSignedInAsync(Profiles.Admin, "Admin do Histórico");
        var admin = adminTokens.AccessToken;
        var player = await factory.CreateSignedInPlayerAsync();
        await using var morning = AppAt("2026-10-28T10:00:00Z");
        await DeactivateAsync(morning, admin, owner.Id, "Abuso");
        await using var afternoon = AppAt("2026-10-28T15:30:00Z");
        (await SearchSlugsAsync(afternoon, owner.Locality)).ShouldBeEmpty();

        var reactivated = await ReactivateAsync(afternoon, admin, owner.Id, "Pedido resolvido");

        reactivated.GetProperty("deactivated").GetBoolean().ShouldBeFalse();
        var login = await LoginAsync(afternoon, owner.Email);
        login.StatusCode.ShouldBe(HttpStatusCode.OK, await login.Content.ReadAsStringAsync(Ct));
        (await SearchSlugsAsync(afternoon, owner.Locality)).ShouldBe([owner.Club.Slug]);
        (await SendAsync(afternoon, HttpMethod.Get, $"/api/clubs/{owner.Club.Slug}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var booked = await SendAsync(afternoon, HttpMethod.Post, "/api/bookings", player.Tokens.AccessToken, new { pitchId = owner.Pitch.Id, startsAt = MondaySlots[1] });
        booked.StatusCode.ShouldBe(HttpStatusCode.Created, await booked.Content.ReadAsStringAsync(Ct));
        // The old session stays ended: the owner signs in again.
        await ProblemAsync(await SendAsync(afternoon, HttpMethod.Post, "/api/auth/refresh", refreshToken: owner.Tokens.RefreshToken), HttpStatusCode.Unauthorized, "invalid_refresh_token");

        var history = (await HistoryAsync(afternoon, admin, owner.Id)).EnumerateArray().ToList();
        history.Select(change => change.GetProperty("action").GetString()).ShouldBe(["reactivated", "deactivated"]);
        history.Select(change => change.GetProperty("reason").GetString()).ShouldBe(["Pedido resolvido", "Abuso"]);
        history.Select(change => change.GetProperty("adminId").GetGuid()).ShouldBe([adminAccount.Id, adminAccount.Id]);
        history.Select(change => change.GetProperty("adminName").GetString()).ShouldBe(["Admin do Histórico", "Admin do Histórico"]);
        history.Select(change => change.GetProperty("at").GetDateTime()).ShouldBe([TestVenues.Utc("2026-10-28T15:30:00Z"), TestVenues.Utc("2026-10-28T10:00:00Z")]);
    }

    [Fact]
    public async Task The_history_of_an_owner_never_changed_is_empty_and_each_owner_has_their_own()
    {
        await using var app = AppAt();
        var changed = await OwnerAsync();
        var untouched = await OwnerAsync();
        var admin = await AdminTokenAsync();
        await DeactivateAsync(app, admin, changed.Id);

        (await HistoryAsync(app, admin, untouched.Id)).GetArrayLength().ShouldBe(0);
        (await HistoryAsync(app, admin, changed.Id)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Reactivating_needs_a_reason_too()
    {
        await using var app = AppAt();
        var owner = await OwnerAsync();
        var admin = await AdminTokenAsync();
        await DeactivateAsync(app, admin, owner.Id);

        var problem = await JsonAsync(await SendAsync(app, HttpMethod.Post, $"/api/admin/owners/{owner.Id}/reactivate", admin, new { reason = " " }), HttpStatusCode.BadRequest);

        problem.GetProperty("errors").GetProperty("reason")[0].GetString().ShouldBe("Indica o motivo.");
        (await AuditAsync(owner.Id)).Count.ShouldBe(1);
    }

    // ---- Repeats and unknown ids

    [Fact]
    public async Task Deactivating_twice_and_reactivating_an_active_owner_are_422_and_write_nothing()
    {
        await using var app = AppAt();
        var owner = await OwnerAsync();
        var admin = await AdminTokenAsync();

        await ProblemAsync(
            await SendAsync(app, HttpMethod.Post, $"/api/admin/owners/{owner.Id}/reactivate", admin, new { reason = "Engano" }), HttpStatusCode.UnprocessableEntity, "owner_not_deactivated");
        (await AuditAsync(owner.Id)).ShouldBeEmpty();

        await DeactivateAsync(app, admin, owner.Id);
        await ProblemAsync(
            await SendAsync(app, HttpMethod.Post, $"/api/admin/owners/{owner.Id}/deactivate", admin, new { reason = "Outra vez" }), HttpStatusCode.UnprocessableEntity, "owner_already_deactivated");

        (await AuditAsync(owner.Id)).Select(row => row.Action).ShouldBe(["Deactivated"]);
    }

    [Fact]
    public async Task Two_admins_deactivating_at_the_same_moment_give_one_change_and_one_422()
    {
        await using var app = AppAt();
        var owner = await OwnerAsync();
        var first = await AdminTokenAsync();
        var second = await AdminTokenAsync();

        var responses = await Task.WhenAll(
            SendAsync(app, HttpMethod.Post, $"/api/admin/owners/{owner.Id}/deactivate", first, new { reason = "Primeiro" }),
            SendAsync(app, HttpMethod.Post, $"/api/admin/owners/{owner.Id}/deactivate", second, new { reason = "Segundo" }));

        responses.Select(response => response.StatusCode).Order().ShouldBe([HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity]);
        (await AuditAsync(owner.Id)).Count.ShouldBe(1);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task An_unknown_id_and_the_id_of_a_player_or_the_admin_are_404_owner_not_found_before_the_body_is_read()
    {
        await using var app = AppAt();
        var admin = await AdminTokenAsync();
        var (player, _) = await factory.CreateSignedInPlayerAsync();
        var (adminAccount, _) = await factory.CreateSignedInAsync(Profiles.Admin);

        foreach (var id in new[] { Guid.CreateVersion7(), player.Id, adminAccount.Id })
        {
            await ProblemAsync(await SendAsync(app, HttpMethod.Get, $"/api/admin/owners/{id}", admin), HttpStatusCode.NotFound, "owner_not_found");
            await ProblemAsync(await SendAsync(app, HttpMethod.Get, $"/api/admin/owners/{id}/history", admin), HttpStatusCode.NotFound, "owner_not_found");
            // A reason that would be refused with 400 does not matter: the id comes first.
            await ProblemAsync(await SendAsync(app, HttpMethod.Post, $"/api/admin/owners/{id}/deactivate", admin, new { reason = "" }), HttpStatusCode.NotFound, "owner_not_found");
            await ProblemAsync(await SendAsync(app, HttpMethod.Post, $"/api/admin/owners/{id}/reactivate", admin, new { reason = "Motivo" }), HttpStatusCode.NotFound, "owner_not_found");
        }

        // Nothing happened to the player or the admin.
        (await AuditAsync(player.Id)).ShouldBeEmpty();
        (await RefreshTokensAsync(player.Id)).Revoked.ShouldBe(0);
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().Where(user => user.Id == player.Id || user.Id == adminAccount.Id).AllAsync(user => user.DeactivatedAt == null, Ct)).ShouldBeTrue();
    }

    // ---- The audit cannot be changed

    [Fact]
    public async Task The_database_rejects_update_delete_and_truncate_on_the_audit_table()
    {
        await using var app = AppAt();
        var owner = await OwnerAsync();
        var admin = await AdminTokenAsync();
        await DeactivateAsync(app, admin, owner.Id, "Original");

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attempts = new (string Name, Func<Task> Run)[]
        {
            ("update reason", async () => await dbContext.Database.ExecuteSqlAsync($"UPDATE owner_status_changes SET reason = 'Alterado' WHERE owner_id = {owner.Id}", Ct)),
            ("update time", async () => await dbContext.Database.ExecuteSqlAsync($"UPDATE owner_status_changes SET created_at = now() WHERE owner_id = {owner.Id}", Ct)),
            ("delete", async () => await dbContext.Database.ExecuteSqlAsync($"DELETE FROM owner_status_changes WHERE owner_id = {owner.Id}", Ct)),
            ("truncate", async () => await dbContext.Database.ExecuteSqlAsync($"TRUNCATE owner_status_changes", Ct)),
        };
        foreach (var (name, run) in attempts)
        {
            var failure = await Should.ThrowAsync<PostgresException>(run, name);
            failure.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation, name);
            failure.MessageText.ShouldContain("append-only");
        }

        var row = (await AuditAsync(owner.Id)).ShouldHaveSingleItem();
        row.Reason.ShouldBe("Original");
    }

    [Fact]
    public async Task An_owner_with_a_history_cannot_be_deleted_from_the_database()
    {
        await using var app = AppAt();
        // No club, so the audit is the only thing that holds the account.
        var ownerId = (await factory.CreateAccountAsync(Profiles.Owner, null, name: "Dono Sem Clubes")).Id;
        var admin = await AdminTokenAsync();
        await DeactivateAsync(app, admin, ownerId);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var failure = await Should.ThrowAsync<PostgresException>(async () =>
            await dbContext.Database.ExecuteSqlAsync($"DELETE FROM users WHERE id = {ownerId}", Ct));

        failure.SqlState.ShouldBe(PostgresErrorCodes.RestrictViolation);
        failure.ConstraintName.ShouldNotBeNull().ShouldStartWith("fk_owner_status_changes_");
    }

    // ---- No player data

    [Fact]
    public async Task The_responses_hold_no_player_name_email_or_id()
    {
        await using var app = AppAt();
        var owner = await OwnerAsync();
        var admin = await AdminTokenAsync();
        var playerEmail = $"jogadora-secreta-{Guid.NewGuid():N}@exemplo.test";
        var player = await factory.CreateAccountAsync(Profiles.Player, null, playerEmail, "Jogadora Secretíssima");
        await factory.AddBookingAsync(owner.Pitch.Id, TestVenues.Utc(MondaySlots[0]), TestVenues.Utc(MondaySlots[1]), player);

        var bodies = new List<string>();
        foreach (var response in new[]
        {
            await SendAsync(app, HttpMethod.Get, $"/api/admin/owners/{owner.Id}", admin),
            await SendAsync(app, HttpMethod.Post, $"/api/admin/owners/{owner.Id}/deactivate", admin, new { reason = "Abuso" }),
            await SendAsync(app, HttpMethod.Get, $"/api/admin/owners/{owner.Id}/history", admin),
            await SendAsync(app, HttpMethod.Post, $"/api/admin/owners/{owner.Id}/reactivate", admin, new { reason = "Resolvido" }),
            await SendAsync(app, HttpMethod.Post, $"/api/admin/owners/{owner.Id}/reactivate", admin, new { reason = "Outra vez" }),
        })
        {
            bodies.Add(await response.Content.ReadAsStringAsync(Ct));
            response.Dispose();
        }

        bodies.Count(body => body.Contains("futureBookings", StringComparison.Ordinal)).ShouldBeGreaterThanOrEqualTo(3);
        foreach (var body in bodies)
        {
            body.ShouldNotContain("Secret", Case.Insensitive);
            body.ShouldNotContain("Secret\\u00ED", Case.Insensitive);
            body.ShouldNotContain(playerEmail, Case.Insensitive);
            body.ShouldNotContain(player.Id.ToString(), Case.Insensitive);
        }
    }
}
