using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SportsBooking.Application.Accounts;
using SportsBooking.Application.Auth;

namespace SportsBooking.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<RegisterAccountRequest>, RegisterAccountRequestValidator>();
        services.AddScoped<AccountRegistrationService>();
        services.AddSingleton<IValidator<LoginRequest>, LoginRequestValidator>();
        services.AddScoped<LoginService>();

        return services;
    }
}
