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

    public static IApplicationBuilder UseOriginCheck(this IApplicationBuilder app) =>
        app.UseMiddleware<OriginCheckMiddleware>();
}
