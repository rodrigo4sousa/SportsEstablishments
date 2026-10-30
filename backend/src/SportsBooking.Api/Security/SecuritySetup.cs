using Microsoft.Extensions.Options;

namespace SportsBooking.Api.Security;

internal static class SecuritySetup
{
    public static IServiceCollection AddOriginCheck(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OriginCheckOptions>()
            .Bind(configuration.GetSection(OriginCheckOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<OriginCheckOptions>, OriginCheckOptionsValidator>();

        return services;
    }

    /// <summary>
    /// Every response, including errors and rejections from later middleware, says the declared content type is to be
    /// trusted (<c>X-Content-Type-Options: nosniff</c>). The header is added when the response starts, so no later step
    /// can leave it out. The API is not served to browsers on its own in production, but its answers pass through the
    /// site's rewrite to the browser, so this holds there too.
    /// </summary>
    public static IApplicationBuilder UseResponseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(static (context, next) =>
        {
            context.Response.OnStarting(static state =>
            {
                ((HttpResponse)state).Headers.XContentTypeOptions = "nosniff";
                return Task.CompletedTask;
            }, context.Response);

            return next(context);
        });

    public static IApplicationBuilder UseOriginCheck(this IApplicationBuilder app) =>
        app.UseMiddleware<OriginCheckMiddleware>();
}
