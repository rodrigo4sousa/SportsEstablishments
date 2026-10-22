namespace SportsBooking.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string ConnectionStringName = "Default";

    public string ConnectionString { get; set; } = string.Empty;
}
