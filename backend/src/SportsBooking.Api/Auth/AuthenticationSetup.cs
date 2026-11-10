using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using SportsBooking.Application.Accounts;
using SportsBooking.Application.Common;
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
                    // US30: a deleted account stops working at once, on every device. Only deletion is checked per request: a deactivated
                    // account keeps the accepted 15 minutes of an access token already issued (US29).
                    OnTokenValidated = async context =>
                    {
                        if (Guid.TryParse(context.Principal?.FindFirst(SessionClaims.Subject)?.Value, out var userId)
                            && await context.HttpContext.RequestServices.GetRequiredService<IAccountStore>().IsDeletedAsync(userId, context.HttpContext.RequestAborted))
                        {
                            context.Fail("The account was deleted.");
                        }
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

        // Who is signed in, for Application code (e.g. OwnedClubs); read from the token, never from the request.
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        return services;
    }
}
