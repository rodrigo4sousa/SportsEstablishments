using Microsoft.Extensions.DependencyInjection;

namespace SportsBooking.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Application services and validators are registered here as stories add them.
        return services;
    }
}
