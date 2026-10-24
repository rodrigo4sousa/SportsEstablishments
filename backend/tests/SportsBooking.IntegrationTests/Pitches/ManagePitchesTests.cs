using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Pitches;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Pitches;

/// <summary>US07 and T02: an owner manages the pitches of their own clubs, and only those.</summary>
public sealed class ManagePitchesTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string PitchesPath(Guid clubId) => $"/api/manage/clubs/{clubId}/pitches";

    private static object Pitch(string name = "Campo 1", string sport = "futsal", decimal price = 12.5m) => new { name, sport, pricePerSlot = price };

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

    private async Task<Guid> CreateClubAsync(string accessToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/manage/clubs", accessToken, new
        {
            name = $"Clube {Guid.NewGuid():N}",
            address = "Rua 1",
            locality = "Porto",
            phone = "912345678",
            contactEmail = "geral@clube.pt",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> CreatePitchAsync(string accessToken, Guid clubId, object? pitch = null)
    {
        using var response = await SendAsync(HttpMethod.Post, PitchesPath(clubId), accessToken, pitch ?? Pitch());
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

    private static async Task<string[]> FieldErrorsAsync(HttpResponseMessage response, string field)
    {
        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        return problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(message => message.GetString()!).ToArray();
    }

    private async Task<Domain.Pitches.Pitch> StoredAsync(Guid pitchId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Pitches.AsNoTracking().SingleAsync(pitch => pitch.Id == pitchId, Ct);
    }

    [Fact]
    public async Task An_owner_creates_an_active_pitch_with_its_sport_and_price()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var clubId = await CreateClubAsync(owner.Tokens.AccessToken);

        using var response = await SendAsync(HttpMethod.Post, PitchesPath(clubId), owner.Tokens.AccessToken, Pitch("  Campo Central ", "padel", 18.75m));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var pitch = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = pitch.GetProperty("id").GetGuid();
        id.Version.ShouldBe(7);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"/api/manage/clubs/{clubId}/pitches/{id}");
        pitch.GetProperty("clubId").GetGuid().ShouldBe(clubId);
        pitch.GetProperty("name").GetString().ShouldBe("Campo Central");
        pitch.GetProperty("sport").GetString().ShouldBe("padel");
        pitch.GetProperty("pricePerSlot").GetDecimal().ShouldBe(18.75m);
        pitch.GetProperty("status").GetString().ShouldBe("active");

        var stored = await StoredAsync(id);
        stored.ClubId.ShouldBe(clubId);
        stored.Sport.ShouldBe(Sport.Padel);
        stored.PricePerSlot.ShouldBe(18.75m);
        stored.Status.ShouldBe(PitchStatus.Active);
    }

    [Fact]
    public async Task Sport_is_stored_as_its_code_text_and_the_price_as_numeric()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var clubId = await CreateClubAsync(owner.Tokens.AccessToken);
        var pitch = await CreatePitchAsync(owner.Tokens.AccessToken, clubId, Pitch("Campo 7", "football7", 30m));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Database
            .SqlQuery<string>($"SELECT sport || '|' || status || '|' || price_per_slot::text AS \"Value\" FROM pitches WHERE id = {pitch.GetProperty("id").GetGuid()}")
            .SingleAsync(Ct);

        row.ShouldBe("football7|active|30.00");
    }

    [Fact]
    public async Task The_club_comes_from_the_route_and_the_status_cannot_be_set_in_the_body()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var clubId = await CreateClubAsync(owner.Tokens.AccessToken);

        var pitch = await CreatePitchAsync(owner.Tokens.AccessToken, clubId, new
        {
            name = "Campo X",
            sport = "tennis",
            pricePerSlot = 9m,
            clubId = Guid.CreateVersion7(),
            status = "inactive",
            id = Guid.CreateVersion7(),
        });

        var stored = await StoredAsync(pitch.GetProperty("id").GetGuid());
        stored.ClubId.ShouldBe(clubId);
        stored.Status.ShouldBe(PitchStatus.Active);
    }

    [Fact]
    public async Task The_list_has_active_and_inactive_pitches_by_name_ignoring_case_one_page_at_a_time()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var clubId = await CreateClubAsync(owner.Tokens.AccessToken);
        var token = owner.Tokens.AccessToken;
        await CreatePitchAsync(token, clubId, Pitch("campo b"));
        var inactive = await CreatePitchAsync(token, clubId, Pitch("Campo C"));
        await CreatePitchAsync(token, clubId, Pitch("Campo A"));
        using (var deactivate = await SendAsync(HttpMethod.Post, $"{PitchesPath(clubId)}/{inactive.GetProperty("id").GetGuid()}/deactivate", token))
        {
            deactivate.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await SendAsync(HttpMethod.Get, PitchesPath(clubId), token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        page.GetProperty("totalCount").GetInt32().ShouldBe(3);
        page.GetProperty("page").GetInt32().ShouldBe(1);
        page.GetProperty("pageSize").GetInt32().ShouldBe(20);
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()).ShouldBe(["Campo A", "campo b", "Campo C"]);
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("status").GetString()).ShouldBe(["active", "active", "inactive"]);

        using var second = await SendAsync(HttpMethod.Get, $"{PitchesPath(clubId)}?page=2&pageSize=2", token);
        var secondPage = await second.Content.ReadFromJsonAsync<JsonElement>(Ct);
        secondPage.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()).ShouldBe(["Campo C"]);
        secondPage.GetProperty("totalCount").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task The_list_only_has_the_pitches_of_that_club()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var token = owner.Tokens.AccessToken;
        var first = await CreateClubAsync(token);
        var second = await CreateClubAsync(token);
        await CreatePitchAsync(token, first, Pitch("Campo 1"));
        await CreatePitchAsync(token, second, Pitch("Campo 2"));

        using var response = await SendAsync(HttpMethod.Get, PitchesPath(first), token);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()).ShouldBe(["Campo 1"]);
    }

    [Fact]
    public async Task A_bad_page_is_a_validation_error()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var clubId = await CreateClubAsync(owner.Tokens.AccessToken);

        using var response = await SendAsync(HttpMethod.Get, $"{PitchesPath(clubId)}?page=0&pageSize=101", owner.Tokens.AccessToken);

        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        problem.GetProperty("errors").TryGetProperty("page", out _).ShouldBeTrue();
        problem.GetProperty("errors").TryGetProperty("pageSize", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task An_owner_reads_a_pitch()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var clubId = await CreateClubAsync(owner.Tokens.AccessToken);
        var created = await CreatePitchAsync(owner.Tokens.AccessToken, clubId, Pitch("Campo 3", "tennis", 8m));

        using var response = await SendAsync(HttpMethod.Get, $"{PitchesPath(clubId)}/{created.GetProperty("id").GetGuid()}", owner.Tokens.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var pitch = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        pitch.GetProperty("id").GetGuid().ShouldBe(created.GetProperty("id").GetGuid());
        pitch.GetProperty("name").GetString().ShouldBe("Campo 3");
        pitch.GetProperty("sport").GetString().ShouldBe("tennis");
        pitch.GetProperty("pricePerSlot").GetDecimal().ShouldBe(8m);
    }

    [Fact]
    public async Task An_owner_edits_name_sport_and_price_but_not_the_status()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var token = owner.Tokens.AccessToken;
        var clubId = await CreateClubAsync(token);
        var created = await CreatePitchAsync(token, clubId);
        var id = created.GetProperty("id").GetGuid();
        using (await SendAsync(HttpMethod.Post, $"{PitchesPath(clubId)}/{id}/deactivate", token))
        {
        }

        using var response = await SendAsync(HttpMethod.Put, $"{PitchesPath(clubId)}/{id}", token, new
        {
            name = "Campo Novo",
            sport = "padel",
            pricePerSlot = 20m,
            status = "active",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var pitch = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        pitch.GetProperty("name").GetString().ShouldBe("Campo Novo");
        pitch.GetProperty("status").GetString().ShouldBe("inactive");
        var stored = await StoredAsync(id);
        stored.Name.ShouldBe("Campo Novo");
        stored.Sport.ShouldBe(Sport.Padel);
        stored.PricePerSlot.ShouldBe(20m);
        stored.Status.ShouldBe(PitchStatus.Inactive);
        stored.UpdatedAt.ShouldBeGreaterThanOrEqualTo(stored.CreatedAt);
    }

    [Fact]
    public async Task A_pitch_is_deactivated_and_reactivated_and_never_deleted()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var token = owner.Tokens.AccessToken;
        var clubId = await CreateClubAsync(token);
        var id = (await CreatePitchAsync(token, clubId)).GetProperty("id").GetGuid();
        var pitchPath = $"{PitchesPath(clubId)}/{id}";

        using var deactivated = await SendAsync(HttpMethod.Post, $"{pitchPath}/deactivate", token);
        deactivated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await deactivated.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString().ShouldBe("inactive");
        (await StoredAsync(id)).Status.ShouldBe(PitchStatus.Inactive);

        using var again = await SendAsync(HttpMethod.Post, $"{pitchPath}/deactivate", token);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var reactivated = await SendAsync(HttpMethod.Post, $"{pitchPath}/reactivate", token);
        reactivated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reactivated.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString().ShouldBe("active");
        (await StoredAsync(id)).Status.ShouldBe(PitchStatus.Active);

        using var delete = await SendAsync(HttpMethod.Delete, pitchPath, token);
        delete.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        (await StoredAsync(id)).Id.ShouldBe(id);
    }

    [Fact]
    public async Task Required_fields_each_get_their_own_message()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var clubId = await CreateClubAsync(owner.Tokens.AccessToken);

        using var response = await SendAsync(HttpMethod.Post, PitchesPath(clubId), owner.Tokens.AccessToken, new { });

        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        var errors = problem.GetProperty("errors");
        errors.GetProperty("name").EnumerateArray().Select(message => message.GetString()).ShouldBe(["Indica o nome do campo."]);
        errors.GetProperty("sport").EnumerateArray().Select(message => message.GetString()).ShouldBe(["Indica o desporto do campo."]);
        errors.GetProperty("pricePerSlot").EnumerateArray().Select(message => message.GetString()).ShouldBe(["O preço por slot tem de ser maior que 0 €."]);
    }

    [Theory]
    [InlineData("rugby")]
    [InlineData("Futsal")]
    [InlineData("futebol 7")]
    public async Task A_sport_outside_the_list_is_rejected_on_the_sport_field(string sport)
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var clubId = await CreateClubAsync(owner.Tokens.AccessToken);

        using var response = await SendAsync(HttpMethod.Post, PitchesPath(clubId), owner.Tokens.AccessToken, Pitch(sport: sport));

        (await FieldErrorsAsync(response, "sport")).ShouldBe(["Escolhe um desporto da lista: futsal, futebol 7, padel ou ténis."]);
    }

    [Theory]
    [InlineData("0", "O preço por slot tem de ser maior que 0 €.")]
    [InlineData("-5", "O preço por slot tem de ser maior que 0 €.")]
    [InlineData("12.345", "O preço por slot pode ter no máximo duas casas decimais.")]
    [InlineData("0.001", "O preço por slot pode ter no máximo duas casas decimais.")]
    [InlineData("500.01", "O preço por slot não pode passar de 500 €.")]
    public async Task A_price_that_is_not_allowed_is_rejected_on_the_price_field(string price, string message)
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var clubId = await CreateClubAsync(owner.Tokens.AccessToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, PitchesPath(clubId))
        {
            // Raw JSON, so the number keeps exactly the digits written (three decimals stay three decimals).
            Content = new StringContent($$"""{"name":"Campo P","sport":"futsal","pricePerSlot":{{price}}}""", System.Text.Encoding.UTF8, "application/json"),
        };
        request.WithCookies((SessionCookies.AccessToken, owner.Tokens.AccessToken));
        using var client = factory.CreateSiteClient();

        using var response = await client.SendAsync(request, Ct);

        (await FieldErrorsAsync(response, "pricePerSlot")).ShouldBe([message]);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("12.5")]
    [InlineData("12.50")]
    [InlineData("500")]
    public async Task Prices_up_to_500_with_at_most_two_decimals_are_accepted(string price)
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var clubId = await CreateClubAsync(owner.Tokens.AccessToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, PitchesPath(clubId))
        {
            Content = new StringContent($$"""{"name":"Campo P","sport":"futsal","pricePerSlot":{{price}}}""", System.Text.Encoding.UTF8, "application/json"),
        };
        request.WithCookies((SessionCookies.AccessToken, owner.Tokens.AccessToken));
        using var client = factory.CreateSiteClient();

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_name_over_the_limit_is_rejected()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var clubId = await CreateClubAsync(owner.Tokens.AccessToken);

        using var response = await SendAsync(HttpMethod.Post, PitchesPath(clubId), owner.Tokens.AccessToken, Pitch(new string('a', 101)));

        (await FieldErrorsAsync(response, "name")).ShouldBe(["O nome pode ter no máximo 100 caracteres."]);
    }

    [Fact]
    public async Task A_name_is_unique_in_its_club_ignoring_case_and_surrounding_spaces()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var token = owner.Tokens.AccessToken;
        var clubId = await CreateClubAsync(token);
        await CreatePitchAsync(token, clubId, Pitch("Campo Ténis"));

        foreach (var duplicate in new[] { "Campo Ténis", "campo ténis", "CAMPO TÉNIS", "  Campo Ténis  " })
        {
            using var response = await SendAsync(HttpMethod.Post, PitchesPath(clubId), token, Pitch(duplicate));
            (await FieldErrorsAsync(response, "name")).ShouldBe(["Já existe um campo com este nome neste clube."]);
        }
    }

    [Fact]
    public async Task The_same_name_is_allowed_in_another_club_even_of_the_same_owner()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var token = owner.Tokens.AccessToken;
        var first = await CreateClubAsync(token);
        var second = await CreateClubAsync(token);
        var otherOwner = await factory.CreateSignedInAsync(Profiles.Owner);
        var thirdClub = await CreateClubAsync(otherOwner.Tokens.AccessToken);
        await CreatePitchAsync(token, first, Pitch("Campo 1"));

        await CreatePitchAsync(token, second, Pitch("Campo 1"));
        await CreatePitchAsync(otherOwner.Tokens.AccessToken, thirdClub, Pitch("campo 1"));
    }

    [Fact]
    public async Task Editing_a_pitch_can_keep_its_own_name_but_not_take_another_pitchs()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var token = owner.Tokens.AccessToken;
        var clubId = await CreateClubAsync(token);
        var one = (await CreatePitchAsync(token, clubId, Pitch("Campo 1"))).GetProperty("id").GetGuid();
        await CreatePitchAsync(token, clubId, Pitch("Campo 2"));

        using var sameName = await SendAsync(HttpMethod.Put, $"{PitchesPath(clubId)}/{one}", token, Pitch("CAMPO 1", "padel", 10m));
        sameName.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var taken = await SendAsync(HttpMethod.Put, $"{PitchesPath(clubId)}/{one}", token, Pitch("campo 2"));
        (await FieldErrorsAsync(taken, "name")).ShouldBe(["Já existe um campo com este nome neste clube."]);
    }

    [Fact]
    public async Task An_inactive_pitch_keeps_its_name_taken()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var token = owner.Tokens.AccessToken;
        var clubId = await CreateClubAsync(token);
        var id = (await CreatePitchAsync(token, clubId, Pitch("Campo 1"))).GetProperty("id").GetGuid();
        using (await SendAsync(HttpMethod.Post, $"{PitchesPath(clubId)}/{id}/deactivate", token))
        {
        }

        using var response = await SendAsync(HttpMethod.Post, PitchesPath(clubId), token, Pitch("Campo 1"));

        (await FieldErrorsAsync(response, "name")).ShouldBe(["Já existe um campo com este nome neste clube."]);
    }

    [Fact]
    public async Task Two_concurrent_creates_with_the_same_name_leave_one_pitch_and_one_validation_error()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var token = owner.Tokens.AccessToken;
        var clubId = await CreateClubAsync(token);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var response = await SendAsync(HttpMethod.Post, PitchesPath(clubId), token, Pitch("Campo Corrida"));
            return response.StatusCode;
        }));

        responses.Count(status => status == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(status => status == HttpStatusCode.BadRequest).ShouldBe(5);
    }

    [Fact]
    public async Task Another_owner_gets_404_on_everything_of_a_club_and_its_pitches()
    {
        var ownerA = await factory.CreateSignedInAsync(Profiles.Owner);
        var ownerB = await factory.CreateSignedInAsync(Profiles.Owner);
        var clubOfA = await CreateClubAsync(ownerA.Tokens.AccessToken);
        var pitchOfA = (await CreatePitchAsync(ownerA.Tokens.AccessToken, clubOfA, Pitch("Campo A"))).GetProperty("id").GetGuid();
        var path = $"{PitchesPath(clubOfA)}/{pitchOfA}";
        var b = ownerB.Tokens.AccessToken;

        var attempts = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Post, PitchesPath(clubOfA), Pitch("Campo B")),
            (HttpMethod.Get, PitchesPath(clubOfA), null),
            (HttpMethod.Get, path, null),
            (HttpMethod.Put, path, Pitch("Roubado")),
            (HttpMethod.Post, $"{path}/deactivate", null),
            (HttpMethod.Post, $"{path}/reactivate", null),
        };
        foreach (var (method, requestPath, body) in attempts)
        {
            using var response = await SendAsync(method, requestPath, b, body);
            await ProblemAsync(response, HttpStatusCode.NotFound, "club_not_found");
        }

        // Nothing of A's changed, and B's invalid body still gets the same 404 (ownership comes before validation).
        using var invalid = await SendAsync(HttpMethod.Post, PitchesPath(clubOfA), b, new { });
        await ProblemAsync(invalid, HttpStatusCode.NotFound, "club_not_found");
        var stored = await StoredAsync(pitchOfA);
        stored.Name.ShouldBe("Campo A");
        stored.Status.ShouldBe(PitchStatus.Active);
        (await ListNamesAsync(ownerA.Tokens.AccessToken, clubOfA)).ShouldBe(["Campo A"]);
    }

    [Fact]
    public async Task A_pitch_asked_for_through_another_club_gets_404_even_for_its_owner()
    {
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var token = owner.Tokens.AccessToken;
        var club = await CreateClubAsync(token);
        var otherClub = await CreateClubAsync(token);
        var pitch = (await CreatePitchAsync(token, club, Pitch("Campo 1"))).GetProperty("id").GetGuid();
        var wrongPath = $"{PitchesPath(otherClub)}/{pitch}";
        var unknownPath = $"{PitchesPath(club)}/{Guid.CreateVersion7()}";

        foreach (var path in new[] { wrongPath, unknownPath })
        {
            var attempts = new (HttpMethod Method, object? Body)[]
            {
                (HttpMethod.Get, null),
                (HttpMethod.Put, Pitch("Outro")),
                (HttpMethod.Post, null),
            };
            foreach (var (method, body) in attempts)
            {
                var target = method == HttpMethod.Post ? $"{path}/deactivate" : path;
                using var response = await SendAsync(method, target, token, body);
                await ProblemAsync(response, HttpStatusCode.NotFound, "pitch_not_found");
            }

            using var reactivate = await SendAsync(HttpMethod.Post, $"{path}/reactivate", token);
            await ProblemAsync(reactivate, HttpStatusCode.NotFound, "pitch_not_found");
        }

        var stored = await StoredAsync(pitch);
        stored.ClubId.ShouldBe(club);
        stored.Status.ShouldBe(PitchStatus.Active);
        stored.Name.ShouldBe("Campo 1");
        (await ListNamesAsync(token, otherClub)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_pitch_through_another_owners_club_is_club_not_found_whatever_the_pitch()
    {
        var ownerA = await factory.CreateSignedInAsync(Profiles.Owner);
        var ownerB = await factory.CreateSignedInAsync(Profiles.Owner);
        var clubOfA = await CreateClubAsync(ownerA.Tokens.AccessToken);
        var clubOfB = await CreateClubAsync(ownerB.Tokens.AccessToken);
        var pitchOfB = (await CreatePitchAsync(ownerB.Tokens.AccessToken, clubOfB)).GetProperty("id").GetGuid();

        // A asks for B's pitch through A's own club, and through B's club.
        using var throughOwn = await SendAsync(HttpMethod.Get, $"{PitchesPath(clubOfA)}/{pitchOfB}", ownerA.Tokens.AccessToken);
        await ProblemAsync(throughOwn, HttpStatusCode.NotFound, "pitch_not_found");
        using var throughTheirs = await SendAsync(HttpMethod.Get, $"{PitchesPath(clubOfB)}/{pitchOfB}", ownerA.Tokens.AccessToken);
        await ProblemAsync(throughTheirs, HttpStatusCode.NotFound, "club_not_found");
    }

    [Fact]
    public async Task A_player_gets_403_and_a_visitor_401_on_every_pitch_endpoint()
    {
        var player = await factory.CreateSignedInPlayerAsync();
        var anyClub = Guid.CreateVersion7();
        var anyPitch = Guid.CreateVersion7();
        var calls = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Post, PitchesPath(anyClub), Pitch()),
            (HttpMethod.Get, PitchesPath(anyClub), null),
            (HttpMethod.Get, $"{PitchesPath(anyClub)}/{anyPitch}", null),
            (HttpMethod.Put, $"{PitchesPath(anyClub)}/{anyPitch}", Pitch()),
            (HttpMethod.Post, $"{PitchesPath(anyClub)}/{anyPitch}/deactivate", null),
            (HttpMethod.Post, $"{PitchesPath(anyClub)}/{anyPitch}/reactivate", null),
        };

        foreach (var (method, path, body) in calls)
        {
            using var asPlayer = await SendAsync(method, path, player.Tokens.AccessToken, body);
            await ProblemAsync(asPlayer, HttpStatusCode.Forbidden, "forbidden");
            using var anonymous = await SendAsync(method, path, accessToken: null, body);
            await ProblemAsync(anonymous, HttpStatusCode.Unauthorized, "unauthorized");
        }
    }

    private async Task<List<string?>> ListNamesAsync(string accessToken, Guid clubId)
    {
        using var response = await SendAsync(HttpMethod.Get, PitchesPath(clubId), accessToken);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToList();
    }
}
