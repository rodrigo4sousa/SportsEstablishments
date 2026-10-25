using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SportsBooking.Application.Accounts;
using SportsBooking.Application.Auth;
using SportsBooking.Application.Bookings;
using SportsBooking.Application.Clubs;
using SportsBooking.Application.Common;
using SportsBooking.Application.Notifications;
using SportsBooking.Application.Pitches;
using SportsBooking.Domain.Accounts;
using SportsBooking.Application.Admin;
using SportsBooking.Infrastructure.Admin;
using SportsBooking.Infrastructure.Auth;
using SportsBooking.Infrastructure.Bookings;
using SportsBooking.Infrastructure.Clubs;
using SportsBooking.Infrastructure.Demo;
using SportsBooking.Infrastructure.Identity;
using SportsBooking.Infrastructure.Notifications;
using SportsBooking.Infrastructure.Pitches;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddOptions<DatabaseOptions>()
            .Configure(options =>
                options.ConnectionString = configuration.GetConnectionString(DatabaseOptions.ConnectionStringName) ?? string.Empty)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                $"ConnectionStrings:{DatabaseOptions.ConnectionStringName} is required.")
            .ValidateOnStart();

        services.AddSingleton<FailedCommandLogger>();
        services.AddDbContext<AppDbContext>((serviceProvider, options) => options
            .UseNpgsql(serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(serviceProvider.GetRequiredService<FailedCommandLogger>())
            // The booking conflict (T03) is expected: BookingStore turns it into a 409 and logs it at Information. EF would
            // log it twice at Error, as a failed command and as a failed SaveChanges. Lower those two events instead of
            // filtering the EF categories: FailedCommandLogger puts the Error back for every other failed command, and a
            // failure nobody handles still reaches the global exception handler, so no real failure goes quiet.
            .ConfigureWarnings(warnings => warnings.Log(
                (CoreEventId.SaveChangesFailed, LogLevel.Debug),
                (RelationalEventId.CommandError, LogLevel.Debug))));

        // The same scoped instance, seen by Application code through its interface.
        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IClubStore, ClubStore>();
        services.AddScoped<IPitchStore, PitchStore>();
        services.AddScoped<IPitchLock, PitchLock>();
        services.AddScoped<IBookingStore, BookingStore>();
        services.AddScoped<IBookingStatistics, BookingStatistics>();
        // T05: only the `seed-demo` command ever asks for it.
        services.AddScoped<DemoDataSeeder>();

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database");

        services.AddAccounts(configuration);
        services.AddSessions(configuration);
        services.AddEmail(configuration);

        return services;
    }

    /// <summary>US22: SMTP sending and the outbox worker that drives it.</summary>
    private static void AddEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>();
        services.AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<OutboxOptions>, OutboxOptionsValidator>();

        // Stateless: a new SMTP connection per message.
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddScoped<OutboxProcessor>();
        services.AddHostedService<OutboxDispatcher>();
    }

    private static void AddSessions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
        services.AddScoped<ISessionService, SessionService>();
    }

    private static void AddAccounts(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AdminSeedOptions>()
            .Bind(configuration.GetSection(AdminSeedOptions.SectionName))
            // Whitespace around the email is a copy-paste slip, not a different address; the rest stays strict.
            .PostConfigure(options => options.Email = options.Email?.Trim())
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AdminSeedOptions>, AdminSeedOptionsValidator>();
        services.AddScoped<AdminAccountSeeder>();
        services.AddScoped<IAccountStore, AccountStore>();
        services.AddSingleton<FallbackPasswordHash>();

        // Identity core only (no UI): PBKDF2 password hashing, user and role stores on EF Core.
        services
            .AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                // The user name is the email, already checked by EmailAddressPolicy; Identity's default character
                // list would reject valid addresses such as o'neill@exemplo.pt.
                options.User.AllowedUserNameCharacters = string.Empty;
                // US01 requires at least 8 characters and nothing else.
                options.Password.RequiredLength = PasswordPolicy.MinimumLength;
                options.Password.RequiredUniqueChars = 1;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                // US03: wrong passwords lock the account for a while (provisional values in LoginLockout).
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = LoginLockout.MaxFailedAttempts;
                options.Lockout.DefaultLockoutTimeSpan = LoginLockout.Duration;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>();

        // Identity v3 hashes are PBKDF2 with HMAC-SHA512; 210,000 iterations is the OWASP recommendation for it.
        // Older hashes with fewer iterations still verify and are rehashed on the next successful password check.
        services.Configure<PasswordHasherOptions>(options =>
        {
            options.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;
            options.IterationCount = 210_000;
        });
    }

    /// <summary>Applies pending EF Core migrations. Development only; production uses a migration bundle.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>Creates the admin account from AdminSeed settings if it does not exist yet (US04).</summary>
    public static async Task SeedAdminAccountAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AdminAccountSeeder>().SeedAsync(cancellationToken);
    }
}
