using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SportsBooking.Application.Common;

namespace SportsBooking.Api.Security;

/// <summary>Rate-limit policy name of the public read endpoints (T06), used with <c>[EnableRateLimiting]</c>.</summary>
internal static class PublicRateLimits
{
    public const string Read = "public-read";
}

/// <summary>Rate-limit policy names, used with <c>[EnableRateLimiting]</c> on the auth endpoints.</summary>
internal static class AuthRateLimits
{
    public const string Login = "auth-login";
    public const string Register = "auth-register";
    public const string Refresh = "auth-refresh";
    public const string PasswordResetRequest = "auth-password-reset-request";
    public const string PasswordResetConfirm = "auth-password-reset-confirm";
}

internal static class RateLimitingSetup
{
    /// <summary>
    /// US03, US05, T06: fixed-window limits per client IP on login, registration, refresh, the two password-reset steps and the public read
    /// endpoints (the built-in middleware), and the forwarded headers that decide the client IP, believed only from configured proxies.
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TrustedProxyOptions>()
            .Bind(configuration.GetSection(TrustedProxyOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TrustedProxyOptions>, TrustedProxyOptionsValidator>();
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<TrustedProxyOptions>>((forwarded, trusted) =>
            {
                forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
                // The defaults trust loopback; only configured proxies are trusted here.
                forwarded.KnownProxies.Clear();
                forwarded.KnownIPNetworks.Clear();
                foreach (var proxy in trusted.Value.KnownProxies)
                {
                    forwarded.KnownProxies.Add(IPAddress.Parse(proxy));
                }

                foreach (var network in trusted.Value.KnownNetworks)
                {
                    forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
                }
            });

        services.AddOptions<AuthRateLimitOptions>()
            .Bind(configuration.GetSection(AuthRateLimitOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AuthRateLimitOptions>, AuthRateLimitOptionsValidator>();
        services.AddOptions<PublicRateLimitOptions>()
            .Bind(configuration.GetSection(PublicRateLimitOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PublicRateLimitOptions>, PublicRateLimitOptionsValidator>();
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<AuthRateLimitOptions>, IOptions<PublicRateLimitOptions>>((limiter, auth, publicRead) =>
            {
                limiter.AddFixedWindowPerClient(PublicRateLimits.Read, publicRead.Value.ReadPermitLimit, publicRead.Value.Window);
                var limits = auth.Value;
                limiter.AddFixedWindowPerClient(AuthRateLimits.Login, limits.LoginPermitLimit, limits.Window);
                limiter.AddFixedWindowPerClient(AuthRateLimits.Register, limits.RegisterPermitLimit, limits.Window);
                limiter.AddFixedWindowPerClient(AuthRateLimits.Refresh, limits.RefreshPermitLimit, limits.Window);
                limiter.AddFixedWindowPerClient(AuthRateLimits.PasswordResetRequest, limits.PasswordResetRequestPermitLimit, limits.Window);
                limiter.AddFixedWindowPerClient(AuthRateLimits.PasswordResetConfirm, limits.PasswordResetConfirmPermitLimit, limits.Window);
                limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                limiter.OnRejected = WriteTooManyRequestsAsync;
            });

        return services;
    }

    private static void AddFixedWindowPerClient(this RateLimiterOptions limiter, string policy, int permitLimit, TimeSpan window) =>
        limiter.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
            ClientPartition.For(context.Connection.RemoteIpAddress),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window, QueueLimit = 0 }));

    private static async ValueTask WriteTooManyRequestsAsync(OnRejectedContext rejected, CancellationToken cancellationToken)
    {
        var context = rejected.HttpContext;
        if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Demasiados pedidos",
                Detail = "Fizeste demasiadas tentativas. Espera um pouco e tenta novamente.",
                Extensions = { ["code"] = ErrorCodes.TooManyRequests },
            },
        });
    }
}
