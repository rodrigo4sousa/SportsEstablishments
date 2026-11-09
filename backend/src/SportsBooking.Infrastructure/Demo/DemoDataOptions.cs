namespace SportsBooking.Infrastructure.Demo;

/// <summary>
/// The <c>DemoData</c> settings. <see cref="AllowInProduction"/> (env <c>DemoData__AllowInProduction=true</c>) is the one way to let the
/// <c>seed-demo</c> command run in the Production environment (T06, a public demo). It does nothing else, and the command still refuses
/// any database that has owners or players. Set it only on the process that runs the command.
/// </summary>
public sealed class DemoDataOptions
{
    public const string SectionName = "DemoData";

    public const string AllowInProductionSetting = "DemoData:AllowInProduction";

    public bool AllowInProduction { get; set; }
}
