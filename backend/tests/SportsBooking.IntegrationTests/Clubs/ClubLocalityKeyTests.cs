using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Clubs;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Clubs;

/// <summary>US16 (provisional): <c>clubs.locality_key</c> is what the public search compares.</summary>
public sealed class ClubLocalityKeyTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> LocalityKeyOfAsync(Guid clubId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
            .SqlQuery<string>($"SELECT locality_key AS \"Value\" FROM clubs WHERE id = {clubId}")
            .SingleAsync(Ct);
    }

    [Fact]
    public async Task The_key_follows_the_locality_when_an_owner_creates_and_edits_a_club()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        using var client = factory.CreateSiteClient();
        var body = new { name = "Clube Chave", address = "Rua 1", locality = "São João da Madeira", phone = "912345678", contactEmail = "geral@clube.pt" };

        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/manage/clubs") { Content = JsonContent.Create(body) };
        create.WithCookies((SessionCookies.AccessToken, owner.Tokens.AccessToken));
        using var created = await client.SendAsync(create, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        (await LocalityKeyOfAsync(id)).ShouldBe("sao joao da madeira");

        using var edit = new HttpRequestMessage(HttpMethod.Put, $"/api/manage/clubs/{id}")
        {
            Content = JsonContent.Create(new { body.name, body.address, locality = "  ÉVORA ", body.phone, body.contactEmail, cancellationWindowHours = 24 }),
        };
        edit.WithCookies((SessionCookies.AccessToken, owner.Tokens.AccessToken));
        using var edited = await client.SendAsync(edit, Ct);
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync(Ct));
        (await LocalityKeyOfAsync(id)).ShouldBe("evora");
    }

    [Fact]
    public async Task The_migration_gives_existing_clubs_the_same_key_as_the_application()
    {
        var localities = new[]
        {
            "Porto", "  PÓRTO  ", "São João da Madeira", "ÉVORA", "Covilhã", "Vila   Nova  de Gaia", "Ílhavo", "Açores", "Câmara de Lobos", "Mértola", "Ponta Delgada",
        };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(factory.ConnectionStringFor($"us16_{Guid.NewGuid():N}"))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new AppDbContext(options);
        var migrations = context.Database.GetMigrations().ToList();
        var target = migrations.Single(migration => migration.EndsWith("_AddClubLocalityKey", StringComparison.Ordinal));
        var migrator = context.GetService<IMigrator>();

        // The schema as it was before the key existed, with clubs written the way the old code wrote them.
        await migrator.MigrateAsync(migrations[migrations.IndexOf(target) - 1], Ct);
        await context.Database.OpenConnectionAsync(Ct);
        await context.Database.ExecuteSqlRawAsync("SET session_replication_role = replica", Ct); // the owners are not needed here
        for (var index = 0; index < localities.Length; index++)
        {
            await context.Database.ExecuteSqlAsync($"""
                INSERT INTO clubs (id, owner_id, slug, name, address, locality, phone, contact_email, cancellation_window_hours, time_zone, created_at, updated_at)
                VALUES ({Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {$"clube-{index}"}, {$"Clube {index}"}, 'Rua 1', {localities[index]}, '912345678', 'geral@clube.pt', 24, 'Europe/Lisbon', now(), now())
                """, Ct);
        }

        await context.Database.ExecuteSqlRawAsync("SET session_replication_role = DEFAULT", Ct);
        await migrator.MigrateAsync(cancellationToken: Ct);

        var stored = await context.Database
            .SqlQuery<StoredClub>($"SELECT locality, locality_key FROM clubs") // snake_case convention: columns follow the property names
            .ToListAsync(Ct);
        stored.Count.ShouldBe(localities.Length);
        foreach (var club in stored)
        {
            club.LocalityKey.ShouldBe(ClubRules.LocalityKey(club.Locality), club.Locality);
        }

        // No leftover default: an insert that forgets the key fails instead of storing an empty one.
        (await context.Database.SqlQuery<string?>($"""
            SELECT column_default AS "Value" FROM information_schema.columns WHERE table_name = 'clubs' AND column_name = 'locality_key'
            """).SingleAsync(Ct)).ShouldBeNull();
    }

    private sealed record StoredClub(string Locality, string LocalityKey);
}
