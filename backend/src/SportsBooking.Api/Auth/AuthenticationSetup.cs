using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Auth;

namespace SportsBooking.Api.Auth;

internal static class AuthenticationSetup
{
    /// <summary>
    /// JWT bearer authentication that reads the access token from the httpOnly cookie, and a fallback policy:
    /// every endpoint needs a signed-in user unless it is marked <c>[AllowAnonymous]</c>.
    /// </summary>
    public static IServiceCollection AddCookieJwtAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = jwt.Value.ValidationParameters();
                bearer.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        context.Token = context.Request.Cookies[AuthCookies.AccessToken];
                        return Task.CompletedTask;
                    },
                };
            });

        // US03: one policy per profile, named after it ([Authorize(Policy = Profiles.Owner)]). A signed-in account of
        // another profile gets 403; without a session, 401. Per-owner checks (whose club it is) are T02.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Profiles.Player, policy => policy.RequireRole(Profiles.Player))
            .AddPolicy(Profiles.Owner, policy => policy.RequireRole(Profiles.Owner))
            .AddPolicy(Profiles.Admin, policy => policy.RequireRole(Profiles.Admin));

        return services;
    }
}
