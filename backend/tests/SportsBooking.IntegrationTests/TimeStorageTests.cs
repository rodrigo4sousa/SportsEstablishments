using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests;

/// <summary>
/// T04: instants are stored as UTC (timestamptz) and local schedule values as date/time without a zone.
/// Uses a test-only model in its own schema so it does not depend on (or add to) the application schema.
/// </summary>
public sealed class TimeStorageTests(ApiFactory factory)
{
    private const string Schema = "t04_time_storage";
    private const string DropSchemaSql = $"DROP SCHEMA IF EXISTS {Schema} CASCADE";

    [Fact]
    public async Task Instants_round_trip_as_utc_even_when_the_session_time_zone_is_not_utc()
    {
        await using var setup = await CreateContextAsync(recreateSchema: true);
        var startsAt = new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc); // first 01:30 in Lisbon
        var id = Guid.CreateVersion7();
        setup.Records.Add(new TimeRecord { Id = id, StartsAt = startsAt, Day = new DateOnly(2026, 10, 25), Opens = new TimeOnly(1, 30) });
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var reader = await CreateContextAsync(recreateSchema: false, sessionTimeZone: "America/New_York");
        var stored = await reader.Records.AsNoTracking().SingleAsync(record => record.Id == id, TestContext.Current.CancellationToken);

        stored.StartsAt.ShouldBe(startsAt);
        stored.StartsAt.Kind.ShouldBe(DateTimeKind.Utc);
        stored.Day.ShouldBe(new DateOnly(2026, 10, 25));
        stored.Opens.ShouldBe(new TimeOnly(1, 30));
    }

    [Fact]
    public async Task Columns_use_timestamptz_for_instants_and_zoneless_types_for_local_schedule_values()
    {
        await using var context = await CreateContextAsync(recreateSchema: true);

        var columnTypes = await context.Database
            .SqlQuery<ColumnType>($"""
                SELECT column_name AS "Name", data_type AS "Type"
                FROM information_schema.columns
                WHERE table_schema = {Schema} AND table_name = 'records'
                """)
            .ToDictionaryAsync(column => column.Name, column => column.Type, TestContext.Current.CancellationToken);

        columnTypes["starts_at"].ShouldBe("timestamp with time zone");
        columnTypes["day"].ShouldBe("date");
        columnTypes["opens"].ShouldBe("time without time zone");
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public async Task Non_utc_instants_are_rejected_on_write(DateTimeKind kind)
    {
        await using var context = await CreateContextAsync(recreateSchema: true);
        context.Records.Add(new TimeRecord
        {
            Id = Guid.CreateVersion7(),
            StartsAt = new DateTime(2026, 7, 15, 10, 0, 0, kind),
            Day = new DateOnly(2026, 7, 15),
            Opens = new TimeOnly(10, 0),
        });

        var exception = await Should.ThrowAsync<Exception>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));

        exception.ToString().ShouldContain("UTC");
    }

    private async Task<TimeStorageDbContext> CreateContextAsync(bool recreateSchema, string? sessionTimeZone = null)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(factory.ConnectionString);
        if (sessionTimeZone is not null)
        {
            connectionString.Timezone = sessionTimeZone;
        }

        var options = new DbContextOptionsBuilder<TimeStorageDbContext>()
            .UseNpgsql(connectionString.ConnectionString)
            .Options;
        var context = new TimeStorageDbContext(options);

        if (recreateSchema)
        {
            await context.Database.ExecuteSqlRawAsync(DropSchemaSql, TestContext.Current.CancellationToken);
            await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(TestContext.Current.CancellationToken);
        }

        return context;
    }

    private sealed record ColumnType(string Name, string Type);

    private sealed class TimeRecord
    {
        public Guid Id { get; set; }

        public DateTime StartsAt { get; set; }

        public DateOnly Day { get; set; }

        public TimeOnly Opens { get; set; }
    }

    private sealed class TimeStorageDbContext(DbContextOptions<TimeStorageDbContext> options) : DbContext(options)
    {
        public DbSet<TimeRecord> Records => Set<TimeRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema(Schema);
            modelBuilder.Entity<TimeRecord>(entity =>
            {
                entity.ToTable("records");
                entity.Property(record => record.Id).HasColumnName("id");
                entity.Property(record => record.StartsAt).HasColumnName("starts_at");
                entity.Property(record => record.Day).HasColumnName("day");
                entity.Property(record => record.Opens).HasColumnName("opens");
            });
        }
    }
}
