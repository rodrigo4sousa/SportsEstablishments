using Microsoft.Extensions.DependencyInjection;

namespace SportsBooking.Infrastructure.Demo;

/// <summary>
/// The <c>seed-demo</c> command of the API (T05): <c>dotnet run --project backend/src/SportsBooking.Api -- seed-demo</c>.
/// It creates the demo data and exits; it never starts the web server, so the email worker never runs. Only that exact
/// argument runs it: the API started any other way never seeds anything. It prints counts only, never an account or a password.
/// </summary>
public static class DemoDataCommand
{
    public const string Name = "seed-demo";

    /// <summary>True when the command line is exactly <c>seed-demo</c>.</summary>
    public static bool IsRequested(IReadOnlyList<string> args) => args.Count == 1 && args[0] == Name;

    /// <summary>Runs the seed in its own scope; returns the exit code: 0 done, 1 refused.</summary>
    public static async Task<int> RunAsync(IServiceProvider services, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        try
        {
            var summary = await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync(cancellationToken);
            await output.WriteLineAsync("Demo data created:");
            await output.WriteLineAsync($"  {summary.Owners} owners, {summary.Players} players, {summary.Clubs} clubs, {summary.Pitches} pitches, {summary.Blocks} blocks");
            await output.WriteLineAsync(
                $"  {summary.Bookings} bookings ({summary.Confirmed} confirmed, {summary.CancelledByPlayer} cancelled by the player, " +
                $"{summary.CancelledByClub} cancelled by the club, {summary.NoShows} no-shows; {summary.CustomerBookings} for customers)");
            await output.WriteLineAsync($"  Accounts: dono01..dono{DemoDataSeeder.OwnerCount:00} and jogador01..jogador{DemoDataSeeder.PlayerCount:00} @{DemoDataSeeder.EmailDomain}; the password is in the README.");
            return 0;
        }
        catch (DemoSeedRefusedException refused)
        {
            await error.WriteLineAsync($"Demo data not created: {refused.Message}");
            return 1;
        }
    }
}
