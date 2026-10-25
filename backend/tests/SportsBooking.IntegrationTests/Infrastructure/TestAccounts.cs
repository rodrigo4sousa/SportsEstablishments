using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SportsBooking.Application.Auth;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Identity;

namespace SportsBooking.IntegrationTests.Infrastructure;

public static class TestAccounts
{
    /// <summary>Creates a player account (without a password, so no slow hashing) and starts a session for it.</summary>
    public static Task<(AppUser User, SessionTokens Tokens)> CreateSignedInPlayerAsync(this ApiFactory factory, string name = "Jogadora de Teste") =>
        factory.CreateSignedInAsync(Profiles.Player, name);

    /// <summary>Creates an account with the given profile (no password) and starts a session for it.</summary>
    public static async Task<(AppUser User, SessionTokens Tokens)> CreateSignedInAsync(
        this WebApplicationFactory<Program> app,
        string profile,
        string name = "Conta de Teste")
    {
        var user = await app.CreateAccountAsync(profile, password: null, name: name);

        await using var scope = app.Services.CreateAsyncScope();
        var tokens = await scope.ServiceProvider.GetRequiredService<ISessionService>()
            .StartAsync(user.Id, profile, TestContext.Current.CancellationToken);
        return (user, tokens);
    }

    /// <summary>
    /// Creates an account directly in the database, with any profile (owners have no sign-up yet, US02).
    /// A password makes it able to log in; it is hashed with the real work factor.
    /// </summary>
    public static async Task<AppUser> CreateAccountAsync(
        this WebApplicationFactory<Program> app,
        string profile,
        string? password,
        string? email = null,
        string name = "Conta de Teste")
    {
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser(email ?? $"conta-{Guid.NewGuid():N}@exemplo.test", name, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var created = password is null ? await users.CreateAsync(user) : await users.CreateAsync(user, password);
        created.Succeeded.ShouldBeTrue();
        (await users.AddToRoleAsync(user, profile)).Succeeded.ShouldBeTrue();
        return user;
    }
}
