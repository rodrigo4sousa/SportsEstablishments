using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Admin;

/// <summary>US24: the platform's totals, on a database that holds only what each test puts there.</summary>
public sealed class AdminStatsTests(ApiFactory factory)
{
    [Fact]
    public async Task An_empty_platform_is_all_zeros()
    {
        await using var admin = await AdminTestApp.CreateAsync(factory);

        var stats = await admin.GetJsonAsync("/api/admin/stats");

        stats.GetProperty("owners").GetProperty("active").GetInt32().ShouldBe(0);
        stats.GetProperty("owners").GetProperty("deactivated").GetInt32().ShouldBe(0);
        stats.GetProperty("clubs").GetProperty("total").GetInt32().ShouldBe(0);
        stats.GetProperty("activePitches").GetInt32().ShouldBe(0);
        stats.GetProperty("players").GetProperty("active").GetInt32().ShouldBe(0);
        stats.GetProperty("confirmedBookings").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Counts_are_right_with_deactivated_owners_and_their_clubs_apart()
    {
        await using var admin = await AdminTestApp.CreateAsync(factory);
        var app = admin.App;

        // Two active owners and one deactivated one.
        var ownerA = await admin.AddOwnerAsync("Dono A");
        var ownerB = await admin.AddOwnerAsync("Dono B");
        var ownerC = await admin.AddOwnerAsync("Dono C");

        // A: one club with an active and an inactive pitch (visible), one club with no pitch at all.
        var clubA1 = await app.CreateClubAsync(ownerA);
        var activeOfA = await app.CreatePitchAsync(clubA1.Id, "Campo 1");
        var inactiveOfA = await app.CreatePitchAsync(clubA1.Id, "Campo 2");
        await app.DeactivatePitchAsync(inactiveOfA.Id);
        await app.CreateClubAsync(ownerA);
        // B: a club whose only pitch is inactive (not visible), and one with an active pitch (visible).
        var clubB1 = await app.CreateClubAsync(ownerB);
        var onlyInactiveOfB = await app.CreatePitchAsync(clubB1.Id);
        await app.DeactivatePitchAsync(onlyInactiveOfB.Id);
        var clubB2 = await app.CreateClubAsync(ownerB);
        var activeOfB = await app.CreatePitchAsync(clubB2.Id);
        // C: a club with an active pitch, and then the owner is deactivated.
        var clubC = await app.CreateClubAsync(ownerC);
        var activeOfC = await app.CreatePitchAsync(clubC.Id);
        await app.DeactivateAccountAsync(ownerC);

        var player1 = await admin.AddPlayerAsync("Jogadora Um");
        var player2 = await admin.AddPlayerAsync("Jogador Dois");
        var player3 = await admin.AddPlayerAsync("Jogadora Três");
        await app.DeactivateAccountAsync(player3);

        const string created = "2026-10-01T10:00:00Z";
        await admin.AddBookingAsync(activeOfA.Id, created, player1);
        await admin.AddBookingAsync(activeOfA.Id, created, player2);
        await admin.AddBookingAsync(activeOfB.Id, created);
        await admin.AddBookingAsync(activeOfB.Id, created, player1, "CancelledByPlayer");
        await admin.AddBookingAsync(activeOfC.Id, created, player2, "CancelledByClub");
        await admin.AddBookingAsync(activeOfC.Id, created, player1, "NoShow");

        var stats = await admin.GetJsonAsync("/api/admin/stats");

        var owners = stats.GetProperty("owners");
        owners.GetProperty("active").GetInt32().ShouldBe(2);
        owners.GetProperty("deactivated").GetInt32().ShouldBe(1);

        var clubs = stats.GetProperty("clubs");
        clubs.GetProperty("total").GetInt32().ShouldBe(5);
        clubs.GetProperty("visible").GetInt32().ShouldBe(2);
        clubs.GetProperty("ofDeactivatedOwners").GetInt32().ShouldBe(1);
        // An active owner's club with nothing to book: A's empty club and B's club with only an inactive pitch.
        clubs.GetProperty("withoutActivePitches").GetInt32().ShouldBe(2);

        // The active pitches of everyone, the deactivated owner's included.
        stats.GetProperty("activePitches").GetInt32().ShouldBe(3);

        var players = stats.GetProperty("players");
        players.GetProperty("active").GetInt32().ShouldBe(2);
        players.GetProperty("deactivated").GetInt32().ShouldBe(1);

        // Two by players and one for a customer; cancelled ones and the no-show are not confirmed.
        stats.GetProperty("confirmedBookings").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task The_admin_is_not_counted_as_an_owner_or_a_player()
    {
        await using var admin = await AdminTestApp.CreateAsync(factory);
        await admin.App.CreateSignedInAsync(SportsBooking.Domain.Accounts.Profiles.Admin, "Outro Administrador");

        var stats = await admin.GetJsonAsync("/api/admin/stats");

        stats.GetProperty("owners").GetProperty("active").GetInt32().ShouldBe(0);
        stats.GetProperty("players").GetProperty("active").GetInt32().ShouldBe(0);
    }
}
