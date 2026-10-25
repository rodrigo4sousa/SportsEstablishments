using System.Text.Json;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Admin;

/// <summary>US26: the list of owners and their numbers, searched, sorted and paged by the database.</summary>
public sealed class AdminOwnersTests(ApiFactory factory)
{
    private sealed record Expected(string Name, string Email, DateTime RegisteredAt, bool Deactivated, int Clubs, int Pitches, int Bookings, int Players)
    {
        public Guid Id { get; set; }
    }

    private static readonly Expected Ana = new("Ana Dono", "z-ana@exemplo.test", new DateTime(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc), false, 1, 2, 4, 2);
    private static readonly Expected Bruno = new("bruno dono", "y-bruno@exemplo.test", new DateTime(2026, 2, 10, 9, 0, 0, DateTimeKind.Utc), false, 2, 1, 2, 1);
    private static readonly Expected Carla = new("Carla Dona", "x-carla@exemplo.test", new DateTime(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc), true, 0, 0, 0, 0);
    private static readonly Expected Diogo = new("Diogo Dono", "w-diogo@exemplo.test", new DateTime(2025, 12, 1, 9, 0, 0, DateTimeKind.Utc), false, 1, 1, 0, 0);

    private static readonly Expected[] All = [Ana, Bruno, Carla, Diogo];

    /// <summary>
    /// Ana: a club with two pitches; her bookings are two by the first player, one by the second, one for a customer (4 bookings, 2
    /// players), and a cancellation by the first and one by a third player that count for nothing. Bruno: two clubs, one pitch;
    /// two bookings by the first player (2 bookings, 1 player). Carla: deactivated, nothing. Diogo: a club and a pitch, no bookings.
    /// </summary>
    private async Task<AdminTestApp> SeedAsync()
    {
        var admin = await AdminTestApp.CreateAsync(factory);
        var app = admin.App;
        foreach (var owner in All)
        {
            owner.Id = await admin.AddOwnerAsync(owner.Name, owner.Email, owner.RegisteredAt);
        }

        var player1 = await admin.AddPlayerAsync("Jogadora Um");
        var player2 = await admin.AddPlayerAsync("Jogador Dois");
        var player3 = await admin.AddPlayerAsync("Jogadora Três");

        var anaClub = await app.CreateClubAsync(Ana.Id);
        var anaPitch1 = await app.CreatePitchAsync(anaClub.Id, "Campo 1");
        var anaPitch2 = await app.CreatePitchAsync(anaClub.Id, "Campo 2");
        const string created = "2026-10-01T10:00:00Z";
        await admin.AddBookingAsync(anaPitch1.Id, created, player1);
        await admin.AddBookingAsync(anaPitch1.Id, created, player1, "NoShow");
        await admin.AddBookingAsync(anaPitch2.Id, created, player2);
        await admin.AddBookingAsync(anaPitch2.Id, created);
        await admin.AddBookingAsync(anaPitch2.Id, created, player1, "CancelledByPlayer");
        await admin.AddBookingAsync(anaPitch1.Id, created, player3, "CancelledByClub");

        var brunoClub = await app.CreateClubAsync(Bruno.Id);
        await app.CreateClubAsync(Bruno.Id);
        var brunoPitch = await app.CreatePitchAsync(brunoClub.Id);
        await admin.AddBookingAsync(brunoPitch.Id, created, player1);
        await admin.AddBookingAsync(brunoPitch.Id, created, player1);

        var diogoClub = await app.CreateClubAsync(Diogo.Id);
        await app.CreatePitchAsync(diogoClub.Id);

        await app.DeactivateAccountAsync(Carla.Id);
        return admin;
    }

    private static Guid[] Ids(JsonElement response) =>
        response.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray();

    /// <summary>The expected order: the column, then the id as PostgreSQL compares uuids (byte by byte, which is the text order of the digits).</summary>
    private static Guid[] Order<TKey>(Func<Expected, TKey> key, bool descending)
    {
        var ordered = descending ? All.OrderByDescending(key) : All.OrderBy(key);
        return ordered.ThenBy(owner => owner.Id.ToString("D"), StringComparer.Ordinal).Select(owner => owner.Id).ToArray();
    }

    [Fact]
    public async Task Each_owner_shows_the_numbers_of_their_clubs_pitches_bookings_and_players()
    {
        await using var admin = await SeedAsync();

        var response = await admin.GetJsonAsync("/api/admin/owners");

        response.GetProperty("totalCount").GetInt32().ShouldBe(4);
        foreach (var item in response.GetProperty("items").EnumerateArray())
        {
            var expected = All.Single(owner => owner.Id == item.GetProperty("id").GetGuid());
            item.GetProperty("name").GetString().ShouldBe(expected.Name);
            item.GetProperty("email").GetString().ShouldBe(expected.Email);
            item.GetProperty("registeredAt").GetDateTime().ShouldBe(expected.RegisteredAt);
            item.GetProperty("deactivated").GetBoolean().ShouldBe(expected.Deactivated);
            item.GetProperty("clubs").GetInt32().ShouldBe(expected.Clubs, expected.Name);
            item.GetProperty("pitches").GetInt32().ShouldBe(expected.Pitches, expected.Name);
            item.GetProperty("bookings").GetInt32().ShouldBe(expected.Bookings, expected.Name);
            item.GetProperty("players").GetInt32().ShouldBe(expected.Players, expected.Name);
        }
    }

    [Fact]
    public async Task The_default_order_is_by_name_ignoring_case()
    {
        await using var admin = await SeedAsync();

        var response = await admin.GetJsonAsync("/api/admin/owners");

        Ids(response).ShouldBe([Ana.Id, Bruno.Id, Carla.Id, Diogo.Id]);
    }

    public static TheoryData<string, bool> EveryColumnBothWays() => new()
    {
        { "name", false }, { "name", true },
        { "email", false }, { "email", true },
        { "registeredAt", false }, { "registeredAt", true },
        { "status", false }, { "status", true },
        { "clubs", false }, { "clubs", true },
        { "pitches", false }, { "pitches", true },
        { "bookings", false }, { "bookings", true },
        { "players", false }, { "players", true },
    };

    [Theory]
    [MemberData(nameof(EveryColumnBothWays))]
    public async Task Every_column_sorts_both_ways_with_the_id_breaking_ties(string sort, bool descending)
    {
        await using var admin = await SeedAsync();

        var response = await admin.GetJsonAsync($"/api/admin/owners?sort={sort}&direction={(descending ? "desc" : "asc")}");

        var expected = sort switch
        {
            "name" => Order(owner => owner.Name.ToLowerInvariant(), descending),
            "email" => Order(owner => owner.Email, descending),
            "registeredAt" => Order(owner => owner.RegisteredAt, descending),
            "status" => Order(owner => owner.Deactivated, descending),
            "clubs" => Order(owner => owner.Clubs, descending),
            "pitches" => Order(owner => owner.Pitches, descending),
            "bookings" => Order(owner => owner.Bookings, descending),
            _ => Order(owner => owner.Players, descending),
        };
        Ids(response).ShouldBe(expected);
    }

    [Fact]
    public async Task Ties_come_in_the_same_order_every_time()
    {
        await using var admin = await SeedAsync();

        var first = Ids(await admin.GetJsonAsync("/api/admin/owners?sort=players&direction=desc"));
        var second = Ids(await admin.GetJsonAsync("/api/admin/owners?sort=players&direction=desc"));

        second.ShouldBe(first);
    }

    [Theory]
    [InlineData("ANA", new[] { "Ana Dono" })]
    [InlineData("dona", new[] { "Carla Dona" })]
    [InlineData("DONO", new[] { "Ana Dono", "bruno dono", "Diogo Dono" })]
    [InlineData("y-bruno@", new[] { "bruno dono" })]
    [InlineData("EXEMPLO.TEST", new[] { "Ana Dono", "bruno dono", "Carla Dona", "Diogo Dono" })]
    [InlineData("  ana  ", new[] { "Ana Dono" })]
    [InlineData("ninguém", new string[0])]
    public async Task The_search_finds_part_of_the_name_or_the_email_ignoring_case(string search, string[] names)
    {
        await using var admin = await SeedAsync();

        var response = await admin.GetJsonAsync($"/api/admin/owners?search={Uri.EscapeDataString(search)}");

        response.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()).ShouldBe(names);
        response.GetProperty("totalCount").GetInt32().ShouldBe(names.Length);
    }

    [Fact]
    public async Task The_search_never_finds_a_player()
    {
        await using var admin = await SeedAsync();

        var response = await admin.GetJsonAsync("/api/admin/owners?search=Jogador");

        response.GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task The_search_and_the_sort_work_together()
    {
        await using var admin = await SeedAsync();

        var response = await admin.GetJsonAsync("/api/admin/owners?search=dono&sort=bookings&direction=desc");

        Ids(response).ShouldBe([Ana.Id, Bruno.Id, Diogo.Id]);
    }

    [Fact]
    public async Task The_list_has_20_to_a_page()
    {
        await using var admin = await AdminTestApp.CreateAsync(factory);
        for (var index = 1; index <= 25; index++)
        {
            await admin.AddOwnerAsync($"Dono {index:00}");
        }

        var first = await admin.GetJsonAsync("/api/admin/owners");
        var second = await admin.GetJsonAsync("/api/admin/owners?page=2");
        var third = await admin.GetJsonAsync("/api/admin/owners?page=3");

        first.GetProperty("items").GetArrayLength().ShouldBe(20);
        first.GetProperty("page").GetInt32().ShouldBe(1);
        first.GetProperty("pageSize").GetInt32().ShouldBe(20);
        first.GetProperty("totalCount").GetInt32().ShouldBe(25);
        second.GetProperty("items").GetArrayLength().ShouldBe(5);
        third.GetProperty("items").GetArrayLength().ShouldBe(0);
        third.GetProperty("totalCount").GetInt32().ShouldBe(25);

        var names = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray())
            .Select(item => item.GetProperty("name").GetString()).ToArray();
        names.ShouldBe(Enumerable.Range(1, 25).Select(index => $"Dono {index:00}").ToArray());
    }

    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("page=-1", "page")]
    [InlineData("page=abc", "page")]
    [InlineData("page=10001", "page")]
    [InlineData("sort=password", "sort")]
    [InlineData("direction=up", "direction")]
    public async Task A_wrong_value_is_a_400_on_that_field(string query, string field)
    {
        await using var admin = await AdminTestApp.CreateAsync(factory);

        var response = await admin.GetProblemAsync($"/api/admin/owners?{query}");

        response.GetProperty("code").GetString().ShouldBe("validation_failed");
        response.GetProperty("errors").EnumerateObject().Select(property => property.Name).ShouldContain(field);
    }

    [Fact]
    public async Task A_search_that_is_too_long_is_a_400()
    {
        await using var admin = await AdminTestApp.CreateAsync(factory);

        var response = await admin.GetProblemAsync($"/api/admin/owners?search={new string('a', 101)}");

        response.GetProperty("errors").EnumerateObject().Select(property => property.Name).ShouldContain("search");
    }
}
