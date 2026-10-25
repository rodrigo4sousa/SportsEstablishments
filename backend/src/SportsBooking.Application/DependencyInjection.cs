using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SportsBooking.Application.Accounts;
using SportsBooking.Application.Admin;
using SportsBooking.Application.Auth;
using SportsBooking.Application.Bookings;
using SportsBooking.Application.Catalog;
using SportsBooking.Application.Clubs;
using SportsBooking.Application.Common;
using SportsBooking.Application.Notifications;
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
        services.AddScoped<BookingEmailOutbox>();
        services.AddScoped<BookingSlotCheck>();
        services.AddScoped<BookingService>();
        services.AddSingleton<MyBookingsRequestValidator>();
        services.AddScoped<MyBookingsService>();
        services.AddScoped<BookingCancellationService>();

        services.AddScoped<OwnedBookings>();
        services.AddSingleton<CancelClubBookingRequestValidator>();
        services.AddSingleton<CreateClubBookingRequestValidator>();
        services.AddScoped<ClubBookingService>();
        services.AddSingleton<CalendarRequestValidator>();
        services.AddScoped<ClubCalendarService>();

        services.AddSingleton<PasswordResetRequestValidator>();
        services.AddSingleton<PasswordResetConfirmRequestValidator>();
        services.AddScoped<PasswordResetService>();

        services.AddScoped<AdminStatsService>();
        services.AddSingleton<BookingsOverTimeRequestValidator>();
        services.AddScoped<AdminBookingsOverTimeService>();
        services.AddSingleton<AdminOwnersRequestValidator>();
        services.AddScoped<AdminOwnersService>();
        services.AddSingleton<OwnerStatusRequestValidator>();
        services.AddScoped<AdminOwnerStatusService>();

        services.AddScoped<VisibleClubs>();
        services.AddSingleton<ClubSearchRequestValidator>();
        services.AddScoped<ClubSearchService>();
        services.AddScoped<PublicClubService>();

        return services;
    }
}
