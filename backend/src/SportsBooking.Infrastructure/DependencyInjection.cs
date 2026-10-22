using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Identity;
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

        services.AddDbContext<AppDbContext>((serviceProvider, options) => options
            .UseNpgsql(serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString)
            .UseSnakeCaseNamingConvention());

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database");

        services.AddAccounts();

        return services;
    }

    private static void AddAccounts(this IServiceCollection services)
    {
        // Identity core only (no UI): PBKDF2 password hashing, user and role stores on EF Core.
        services
            .AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                // US01 requires at least 8 characters and nothing else.
                options.Password.RequiredLength = PasswordPolicy.MinimumLength;
                options.Password.RequiredUniqueChars = 1;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>();
    }

    /// <summary>Applies pending EF Core migrations. Development only; production uses a migration bundle.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
