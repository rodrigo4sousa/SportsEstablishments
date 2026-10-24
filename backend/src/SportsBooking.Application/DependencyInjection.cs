using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SportsBooking.Application.Accounts;
using SportsBooking.Application.Auth;
using SportsBooking.Application.Bookings;
using SportsBooking.Application.Catalog;
using SportsBooking.Application.Clubs;
using SportsBooking.Application.Common;
using SportsBooking.Application.Pitches;

namespace SportsBooking.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<RegisterAccountRequest>, RegisterAccountRequestValidator>();
        services.AddScoped<AccountRegistrationService>();
        services.AddSingleton<IValidator<LoginRequest>, LoginRequestValidator>();
        services.AddScoped<LoginService>();

        services.AddSingleton<IValidator<PageRequest>, PageRequestValidator>();
        services.AddSingleton<CreateClubRequestValidator>();
        services.AddSingleton<UpdateClubRequestValidator>();
        services.AddScoped<OwnedClubs>();
        services.AddScoped<ClubService>();

        services.AddSingleton<PitchRequestValidator>();
        services.AddScoped<OwnedPitches>();
        services.AddScoped<PitchService>();

        services.AddScoped<PitchDayCalendar>();
        services.AddSingleton<PitchScheduleRequestValidator>();
        services.AddScoped<PitchScheduleService>();

        services.AddSingleton<BlockCheckValidator>();
        services.AddScoped<BlockService>();

        services.AddSingleton<CreateBookingRequestValidator>();
        services.AddScoped<BookingService>();

        services.AddScoped<VisibleClubs>();
        services.AddSingleton<ClubSearchRequestValidator>();
        services.AddScoped<ClubSearchService>();

        return services;
    }
}
