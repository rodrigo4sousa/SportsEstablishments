using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SportsBooking.Application.Auth;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Identity;

namespace SportsBooking.IntegrationTests.Infrastructure;

public static class TestAccounts
{
    /// <summary>Creates a player account (without a password, so no slow hashing) and starts a session for it.</summary>
    public static async Task<(AppUser User, SessionTokens Tokens)> CreateSignedInPlayerAsync(this ApiFactory factory, string name = "Jogadora de Teste")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser($"sessao-{Guid.NewGuid():N}@exemplo.test", name);
        (await users.CreateAsync(user)).Succeeded.ShouldBeTrue();
        (await users.AddToRoleAsync(user, Profiles.Player)).Succeeded.ShouldBeTrue();

        var tokens = await scope.ServiceProvider.GetRequiredService<ISessionService>()
            .StartAsync(user.Id, Profiles.Player, TestContext.Current.CancellationToken);
        return (user, tokens);
    }
}
