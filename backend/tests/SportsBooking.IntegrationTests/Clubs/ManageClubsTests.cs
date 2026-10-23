using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Clubs;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Clubs;

/// <summary>US06 and T02: an owner creates, lists, reads and edits their own clubs, and only those.</summary>
public sealed class ManageClubsTests(ApiFactory factory)
{
    private const string ClubsPath = "/api/manage/clubs";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Club(string name = "Clube de Ténis da Foz", int? cancellationWindowHours = null) => new
    {
        name,
        address = "Rua do Mar, 1",
        locality = "Porto",
        phone = "912 345 678",
        contactEmail = "geral@tenisfoz.pt",
        cancellationWindowHours,
    };

    private async Task<string> SignedInOwnerAsync() => (await factory.CreateSignedInAsync(Profiles.Owner)).Tokens.AccessToken;

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? accessToken, object? body = null)
    {
        using var client = factory.CreateSiteClient();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (accessToken is not null)
        {
            request.WithCookies((SessionCookies.AccessToken, accessToken));
        }

        return await client.SendAsync(request, Ct);
    }

    private async Task<JsonElement> CreateAsync(string accessToken, object club)
    {
        using var response = await SendAsync(HttpMethod.Post, ClubsPath, accessToken, club);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        return problem;
    }

    private async Task<Club> StoredAsync(Guid clubId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Clubs.AsNoTracking().SingleAsync(club => club.Id == clubId, Ct);
    }

    /// <summary>A name no other test uses, so slugs are predictable in a shared database.</summary>
    private static string UniqueName(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    [Fact]
    public async Task An_owner_creates_a_club_with_a_slug_the_default_window_and_the_lisbon_time_zone()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var name = UniqueName("Clube de Ténis da Foz");

        using var response = await SendAsync(HttpMethod.Post, ClubsPath, owner.Tokens.AccessToken, Club(name));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var club = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = club.GetProperty("id").GetGuid();
        id.Version.ShouldBe(7);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"/api/manage/clubs/{id}");
        club.GetProperty("name").GetString().ShouldBe(name);
        club.GetProperty("slug").GetString().ShouldBe(ClubSlug.FromName(name));
        club.GetProperty("slug").GetString().ShouldStartWith("clube-de-tenis-da-foz-");
        club.GetProperty("address").GetString().ShouldBe("Rua do Mar, 1");
        club.GetProperty("locality").GetString().ShouldBe("Porto");
        club.GetProperty("phone").GetString().ShouldBe("912345678");
        club.GetProperty("contactEmail").GetString().ShouldBe("geral@tenisfoz.pt");
        club.GetProperty("cancellationWindowHours").GetInt32().ShouldBe(24);
        club.GetProperty("timeZone").GetString().ShouldBe("Europe/Lisbon");

        var stored = await StoredAsync(id);
        stored.OwnerId.ShouldBe(owner.User.Id);
        stored.TimeZoneId.ShouldBe("Europe/Lisbon");
    }

    [Fact]
    public async Task The_owner_is_taken_from_the_session_and_never_from_the_body()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var other = await factory.CreateSignedInAsync(Profiles.Owner);

        var club = await CreateAsync(owner.Tokens.AccessToken, new
        {
            name = UniqueName("Padel"),
            address = "Rua 1",
            locality = "Lisboa",
            phone = "912345678",
            contactEmail = "geral@padel.pt",
            ownerId = other.User.Id,
            slug = "escolhido-pelo-cliente",
            timeZone = "America/New_York",
            id = Guid.CreateVersion7(),
        });

        var stored = await StoredAsync(club.GetProperty("id").GetGuid());
        stored.OwnerId.ShouldBe(owner.User.Id);
        stored.Slug.ShouldNotBe("escolhido-pelo-cliente");
        stored.TimeZoneId.ShouldBe("Europe/Lisbon");
    }

    [Fact]
    public async Task A_name_already_taken_gets_a_numeric_suffix_across_owners()
    {
        var name = UniqueName("Padel & Co.");
        var baseSlug = ClubSlug.FromName(name);

        var first = await CreateAsync(await SignedInOwnerAsync(), Club(name));
        var second = await CreateAsync(await SignedInOwnerAsync(), Club(name));
        var third = await CreateAsync(await SignedInOwnerAsync(), Club(name.ToUpperInvariant()));

        first.GetProperty("slug").GetString().ShouldBe(baseSlug);
        second.GetProperty("slug").GetString().ShouldBe($"{baseSlug}-2");
        third.GetProperty("slug").GetString().ShouldBe($"{baseSlug}-3");
    }

    [Fact]
    public async Task Concurrent_creates_with_the_same_name_get_distinct_slugs()
    {
        var name = UniqueName("Clube Desportivo do Porto");
        var baseSlug = ClubSlug.FromName(name);
        var owners = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => SignedInOwnerAsync()));

        var responses = await Task.WhenAll(owners.Select(owner => SendAsync(HttpMethod.Post, ClubsPath, owner, Club(name))));

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.Created);
        var slugs = new List<string>();
        foreach (var response in responses)
        {
            slugs.Add((await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("slug").GetString()!);
            response.Dispose();
        }

        slugs.Distinct().Count().ShouldBe(6);
        slugs.ShouldBe([baseSlug, .. Enumerable.Range(2, 5).Select(suffix => $"{baseSlug}-{suffix}")], ignoreOrder: true);
    }

    [Fact]
    public async Task An_owner_reads_and_edits_a_club_and_the_slug_survives_a_rename()
    {
        var owner = await SignedInOwnerAsync();
        var created = await CreateAsync(owner, Club(UniqueName("Clube de Ténis da Foz")));
        var id = created.GetProperty("id").GetGuid();
        var slug = created.GetProperty("slug").GetString();

        using var update = await SendAsync(HttpMethod.Put, $"{ClubsPath}/{id}", owner, new
        {
            name = "  Foz Ténis & Padel ",
            address = "Avenida Brasil, 100",
            locality = "Matosinhos",
            phone = "+351 229 000 000",
            contactEmail = "reservas@foz.pt",
            cancellationWindowHours = 6,
        });

        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await update.Content.ReadFromJsonAsync<JsonElement>(Ct);
        updated.GetProperty("name").GetString().ShouldBe("Foz Ténis & Padel");
        updated.GetProperty("slug").GetString().ShouldBe(slug);
        updated.GetProperty("cancellationWindowHours").GetInt32().ShouldBe(6);

        using var get = await SendAsync(HttpMethod.Get, $"{ClubsPath}/{id}", owner);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var read = await get.Content.ReadFromJsonAsync<JsonElement>(Ct);
        read.GetRawText().ShouldBe(updated.GetRawText());
        read.GetProperty("address").GetString().ShouldBe("Avenida Brasil, 100");
        read.GetProperty("locality").GetString().ShouldBe("Matosinhos");
        read.GetProperty("phone").GetString().ShouldBe("+351229000000");
        read.GetProperty("contactEmail").GetString().ShouldBe("reservas@foz.pt");
        read.GetProperty("timeZone").GetString().ShouldBe("Europe/Lisbon");
        (await StoredAsync(id)).UpdatedAt.ShouldBeGreaterThan((await StoredAsync(id)).CreatedAt);
    }

    [Fact]
    public async Task Renaming_a_club_frees_nothing_and_takes_nothing()
    {
        var owner = await SignedInOwnerAsync();
        var oldName = UniqueName("Campos da Luz");
        var club = await CreateAsync(owner, Club(oldName));
        using (var rename = await SendAsync(HttpMethod.Put, $"{ClubsPath}/{club.GetProperty("id").GetGuid()}", owner, Club("Outro Nome", 24)))
        {
            rename.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // The old slug stays with the renamed club, so a new club with the old name gets a suffix.
        var newcomer = await CreateAsync(owner, Club(oldName));

        newcomer.GetProperty("slug").GetString().ShouldBe($"{ClubSlug.FromName(oldName)}-2");
    }

    [Fact]
    public async Task The_list_holds_only_the_owners_clubs_by_name_one_page_at_a_time()
    {
        var owner = await SignedInOwnerAsync();
        var other = await SignedInOwnerAsync();
        var otherClub = await CreateAsync(other, Club(UniqueName("Alfa do Outro Dono")));
        foreach (var name in new[] { "Delta", "alfa", "Charlie", "Bravo", "Echo" })
        {
            await CreateAsync(owner, Club($"{name} {Guid.NewGuid():N}"));
        }

        using var firstPage = await SendAsync(HttpMethod.Get, $"{ClubsPath}?page=1&pageSize=2", owner);
        using var lastPage = await SendAsync(HttpMethod.Get, $"{ClubsPath}?page=3&pageSize=2", owner);
        using var everything = await SendAsync(HttpMethod.Get, ClubsPath, owner);

        var first = await firstPage.Content.ReadFromJsonAsync<JsonElement>(Ct);
        first.GetProperty("page").GetInt32().ShouldBe(1);
        first.GetProperty("pageSize").GetInt32().ShouldBe(2);
        first.GetProperty("totalCount").GetInt32().ShouldBe(5);
        Names(first).Select(name => name.Split(' ')[0]).ShouldBe(["alfa", "Bravo"]);
        Names(await lastPage.Content.ReadFromJsonAsync<JsonElement>(Ct)).Select(name => name.Split(' ')[0]).ShouldBe(["Echo"]);

        var all = await everything.Content.ReadFromJsonAsync<JsonElement>(Ct);
        all.GetProperty("pageSize").GetInt32().ShouldBe(20);
        all.GetProperty("items").EnumerateArray().Select(club => club.GetProperty("id").GetGuid())
            .ShouldNotContain(otherClub.GetProperty("id").GetGuid());
        Names(all).Select(name => name.Split(' ')[0]).ShouldBe(["alfa", "Bravo", "Charlie", "Delta", "Echo"]);

        static IEnumerable<string> Names(JsonElement page) =>
            page.GetProperty("items").EnumerateArray().Select(club => club.GetProperty("name").GetString()!);
    }

    [Fact]
    public async Task An_owner_without_clubs_gets_an_empty_list()
    {
        using var response = await SendAsync(HttpMethod.Get, ClubsPath, await SignedInOwnerAsync());

        var page = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        page.GetProperty("items").GetArrayLength().ShouldBe(0);
        page.GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=101", "pageSize")]
    public async Task Invalid_paging_is_400_per_field(string query, string field)
    {
        using var response = await SendAsync(HttpMethod.Get, $"{ClubsPath}?{query}", await SignedInOwnerAsync());

        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        problem.GetProperty("errors").EnumerateObject().Select(property => property.Name).ShouldBe([field]);
    }

    [Fact]
    public async Task Invalid_fields_are_400_validation_failed_with_a_pt_pt_message_per_field_and_create_nothing()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);

        using var response = await SendAsync(HttpMethod.Post, ClubsPath, owner.Tokens.AccessToken, new
        {
            name = " ",
            address = new string('a', 201),
            locality = "",
            phone = "123",
            contactEmail = "geral@clube",
            cancellationWindowHours = 0,
        });

        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        var errors = problem.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
        errors.Keys.ShouldBe(["name", "address", "locality", "phone", "contactEmail", "cancellationWindowHours"], ignoreOrder: true);
        errors["name"].ShouldBe(["Indica o nome do clube."]);
        errors["address"].ShouldBe(["A morada pode ter no máximo 200 caracteres."]);
        errors["locality"].ShouldBe(["Indica a localidade do clube."]);
        errors["phone"].ShouldBe(["Indica um número de telefone válido, com 9 a 15 algarismos."]);
        errors["contactEmail"].ShouldBe(["Indica um email válido, por exemplo nome@exemplo.pt."]);
        errors["cancellationWindowHours"].ShouldBe(["A janela de cancelamento tem de estar entre 1 e 168 horas."]);

        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Clubs.CountAsync(club => club.OwnerId == owner.User.Id, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Missing_fields_get_the_same_messages_as_empty_ones_and_editing_needs_the_window()
    {
        var owner = await SignedInOwnerAsync();
        var club = await CreateAsync(owner, Club(UniqueName("Clube")));

        using var create = await SendAsync(HttpMethod.Post, ClubsPath, owner, new { });
        using var update = await SendAsync(HttpMethod.Put, $"{ClubsPath}/{club.GetProperty("id").GetGuid()}", owner, Club("Novo nome"));

        var createErrors = (await ProblemAsync(create, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed))
            .GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
        createErrors.Keys.ShouldBe(["name", "address", "locality", "phone", "contactEmail"], ignoreOrder: true);
        var updateErrors = (await ProblemAsync(update, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed))
            .GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
        updateErrors["cancellationWindowHours"].ShouldBe(["Indica a janela de cancelamento, em horas."]);
    }

    [Fact]
    public async Task Another_owners_club_is_404_on_get_and_update_and_stays_untouched()
    {
        var ownerA = await SignedInOwnerAsync();
        var ownerB = await SignedInOwnerAsync();
        var clubOfB = await CreateAsync(ownerB, Club(UniqueName("Clube do Dono B")));
        var id = clubOfB.GetProperty("id").GetGuid();
        var before = await StoredAsync(id);

        using var get = await SendAsync(HttpMethod.Get, $"{ClubsPath}/{id}", ownerA);
        using var update = await SendAsync(HttpMethod.Put, $"{ClubsPath}/{id}", ownerA, Club("Roubado", 1));
        using var invalidUpdate = await SendAsync(HttpMethod.Put, $"{ClubsPath}/{id}", ownerA, new { });
        using var missing = await SendAsync(HttpMethod.Get, $"{ClubsPath}/{Guid.CreateVersion7()}", ownerA);

        var forGet = await ProblemAsync(get, HttpStatusCode.NotFound, ErrorCodes.ClubNotFound);
        await ProblemAsync(update, HttpStatusCode.NotFound, ErrorCodes.ClubNotFound);
        await ProblemAsync(invalidUpdate, HttpStatusCode.NotFound, ErrorCodes.ClubNotFound);
        var forMissing = await ProblemAsync(missing, HttpStatusCode.NotFound, ErrorCodes.ClubNotFound);
        // Another owner's club and a club that does not exist are indistinguishable.
        forGet.GetProperty("detail").GetString().ShouldBe(forMissing.GetProperty("detail").GetString());
        forGet.GetProperty("title").GetString().ShouldBe(forMissing.GetProperty("title").GetString());

        var after = await StoredAsync(id);
        after.Name.ShouldBe(before.Name);
        after.CancellationWindowHours.ShouldBe(before.CancellationWindowHours);
        after.UpdatedAt.ShouldBe(before.UpdatedAt);
    }

    [Fact]
    public async Task Club_instants_are_stored_as_utc_with_the_club_time_zone()
    {
        // T04 storage test, repeated on the clubs table (US06).
        var created = await CreateAsync(await SignedInOwnerAsync(), Club(UniqueName("Clube")));
        var id = created.GetProperty("id").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var columns = await dbContext.Database
            .SqlQuery<ColumnType>($"""
                SELECT column_name AS "Name", data_type AS "Type"
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'clubs'
                """)
            .ToDictionaryAsync(column => column.Name, column => column.Type, Ct);
        var stored = await dbContext.Clubs.AsNoTracking().SingleAsync(club => club.Id == id, Ct);

        columns["created_at"].ShouldBe("timestamp with time zone");
        columns["updated_at"].ShouldBe("timestamp with time zone");
        stored.CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
        stored.CreatedAt.ShouldBe(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        stored.TimeZoneId.ShouldBe("Europe/Lisbon");
    }

    private sealed record ColumnType(string Name, string Type);
}
