using System.Globalization;
using SportsBooking.Domain.Pitches;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Infrastructure.Demo;

/// <summary>The two areas the demo clubs are in; most players book in their own.</summary>
public enum Region
{
    Porto,
    Lisboa,
}

internal sealed record PitchSpec(
    string Name,
    Sport Sport,
    decimal Price,
    int SlotMinutes,
    double Popularity,
    string WeekdayOpens = "09:00",
    string WeekdayCloses = "23:00",
    string WeekendOpens = "08:00",
    string WeekendCloses = "22:00",
    DayOfWeek? ClosedOn = null)
{
    /// <summary>The week as <see cref="Pitch.ChangeSchedule"/> takes it.</summary>
    public Dictionary<DayOfWeek, OpeningHours?> Week() =>
        Enum.GetValues<DayOfWeek>().ToDictionary(
            day => day,
            day => day == ClosedOn
                ? null
                : day is DayOfWeek.Saturday or DayOfWeek.Sunday
                    ? Hours(WeekendOpens, WeekendCloses)
                    : Hours(WeekdayOpens, WeekdayCloses));

    private static OpeningHours Hours(string opens, string closes) =>
        OpeningHours.Create(TimeOnly.Parse(opens, CultureInfo.InvariantCulture), TimeOnly.Parse(closes, CultureInfo.InvariantCulture));
}

internal sealed record ClubSpec(
    string Name,
    string Address,
    string Locality,
    string Phone,
    int OwnerIndex,
    Region Region,
    int CancellationWindowHours,
    IReadOnlyList<PitchSpec> Pitches);

/// <summary>A block: a day (counted from today), a time range in Lisbon time and why. The past ones are history; the future ones show in the calendar.</summary>
internal sealed record BlockSpec(int ClubIndex, int PitchIndex, int DaysFromToday, TimeOnly Starts, TimeOnly Ends, string Reason);

/// <summary>The demo clubs (T05): ten, around Porto and Lisboa, with all four sports, realistic prices, hours and slot lengths.</summary>
internal static class DemoCatalog
{
    public static readonly IReadOnlyList<ClubSpec> Clubs =
    [
        new("Padel Foz Clube", "Rua do Passeio Alegre 120", "Porto", "226100101", 0, Region.Porto, 24,
        [
            new("Padel 1", Sport.Padel, 27m, 90, 1.15),
            new("Padel 2", Sport.Padel, 27m, 90, 1.10),
            new("Padel 3", Sport.Padel, 30m, 90, 0.95),
            new("Padel 4", Sport.Padel, 30m, 90, 0.85),
            new("Ténis 1", Sport.Tennis, 12m, 60, 0.90),
            new("Ténis 2", Sport.Tennis, 12m, 60, 0.75),
        ]),
        new("Arena Futsal Boavista", "Avenida da Boavista 2450", "Porto", "226100102", 0, Region.Porto, 12,
        [
            new("Futsal A", Sport.Futsal, 38m, 60, 1.20, WeekdayCloses: "00:00"),
            new("Futsal B", Sport.Futsal, 38m, 60, 1.00, WeekdayCloses: "00:00"),
            new("Futebol 7", Sport.Football7, 60m, 60, 0.95, WeekdayCloses: "00:00"),
        ]),
        new("Clube de Ténis do Bonfim", "Rua do Bonfim 301", "Porto", "226100103", 1, Region.Porto, 48,
        [
            new("Saibro 1", Sport.Tennis, 14m, 60, 1.05, "10:00", "22:00", "09:00", "20:00", DayOfWeek.Monday),
            new("Saibro 2", Sport.Tennis, 14m, 60, 0.95, "10:00", "22:00", "09:00", "20:00", DayOfWeek.Monday),
            new("Saibro 3", Sport.Tennis, 14m, 60, 0.80, "10:00", "22:00", "09:00", "20:00", DayOfWeek.Monday),
            new("Piso rápido", Sport.Tennis, 16m, 60, 1.00, "10:00", "22:00", "09:00", "20:00", DayOfWeek.Monday),
        ]),
        new("Complexo Desportivo Gaia Sul", "Rua Conselheiro Veloso da Cruz 15", "Vila Nova de Gaia", "223100104", 1, Region.Porto, 24,
        [
            new("Relvado 1", Sport.Football7, 55m, 60, 1.10),
            new("Relvado 2", Sport.Football7, 55m, 60, 0.90),
            new("Futsal 1", Sport.Futsal, 35m, 60, 1.00),
            new("Padel 1", Sport.Padel, 24m, 90, 0.95),
        ]),
        new("Matosinhos Sports Center", "Avenida Serpa Pinto 88", "Matosinhos", "229100105", 2, Region.Porto, 24,
        [
            new("Padel 1", Sport.Padel, 26m, 90, 1.10),
            new("Padel 2", Sport.Padel, 26m, 90, 1.00),
            new("Padel 3", Sport.Padel, 26m, 90, 0.85),
            new("Futsal 1", Sport.Futsal, 36m, 60, 1.05, WeekdayCloses: "00:00"),
            new("Futsal 2", Sport.Futsal, 36m, 60, 0.90, WeekdayCloses: "00:00"),
        ]),
        new("Maia Padel & Ténis", "Rua Padre Américo 42", "Maia", "229100106", 2, Region.Porto, 24,
        [
            new("Padel 1", Sport.Padel, 18m, 60, 1.00),
            new("Padel 2", Sport.Padel, 18m, 60, 0.90),
            new("Ténis 1", Sport.Tennis, 12m, 60, 0.85),
            new("Ténis 2 (meia hora)", Sport.Tennis, 7m, 30, 0.80),
        ]),
        new("Lisboa Padel Club", "Avenida da República 54", "Lisboa", "213100107", 3, Region.Lisboa, 24,
        [
            new("Padel 1", Sport.Padel, 30m, 90, 1.20),
            new("Padel 2", Sport.Padel, 30m, 90, 1.10),
            new("Padel 3", Sport.Padel, 32m, 90, 1.00),
            new("Padel 4", Sport.Padel, 32m, 90, 0.90),
        ]),
        new("Futsal Alvalade", "Rua Cidade de Bolama 9", "Lisboa", "213100108", 3, Region.Lisboa, 12,
        [
            new("Futsal 1", Sport.Futsal, 42m, 60, 1.20, WeekdayCloses: "00:00"),
            new("Futsal 2", Sport.Futsal, 42m, 60, 1.10, WeekdayCloses: "00:00"),
            new("Futsal 3", Sport.Futsal, 40m, 60, 0.90, WeekdayCloses: "00:00"),
            new("Futebol 7", Sport.Football7, 65m, 60, 1.00, WeekdayCloses: "00:00"),
        ]),
        new("Oeiras Raquetes", "Rua Dr. António Martins 21", "Oeiras", "214100109", 4, Region.Lisboa, 48,
        [
            new("Ténis 1", Sport.Tennis, 15m, 60, 1.00),
            new("Ténis 2", Sport.Tennis, 15m, 60, 0.90),
            new("Ténis 3", Sport.Tennis, 15m, 60, 0.75),
            new("Padel 1", Sport.Padel, 28m, 90, 1.05),
            new("Padel 2", Sport.Padel, 28m, 90, 0.95),
        ]),
        new("Cascais Arena", "Avenida Marginal 700", "Cascais", "214100110", 4, Region.Lisboa, 24,
        [
            new("Futebol 7 A", Sport.Football7, 70m, 60, 1.10),
            new("Futebol 7 B", Sport.Football7, 70m, 60, 0.95),
            new("Futsal 1", Sport.Futsal, 45m, 60, 1.00),
            new("Padel 1", Sport.Padel, 32m, 90, 1.00),
        ]),
    ];

    /// <summary>A few blocks: two that are over and three to come, one of them the future maintenance block of a padel court.</summary>
    public static readonly IReadOnlyList<BlockSpec> Blocks =
    [
        new(0, 0, -20, new TimeOnly(10, 0), new TimeOnly(18, 0), "Torneio interno do clube"),
        new(3, 0, -33, new TimeOnly(8, 0), new TimeOnly(22, 0), "Manutenção do relvado"),
        new(6, 0, 5, new TimeOnly(9, 0), new TimeOnly(22, 30), "Manutenção da superfície"),
        new(4, 1, 9, new TimeOnly(9, 0), new TimeOnly(15, 0), "Obras na iluminação"),
        new(9, 0, 12, new TimeOnly(10, 0), new TimeOnly(20, 0), "Torneio de verão"),
    ];

    /// <summary>Why a club cancels a booking.</summary>
    public static readonly string[] ClubReasons =
    [
        "Campo em manutenção",
        "Avaria na iluminação",
        "Chuva forte: o piso ficou impraticável",
        "Torneio do clube nesse horário",
        "Obras no balneário e no campo",
    ];
}

/// <summary>Names and addresses of the demo accounts. Every address is on <c>example.test</c>.</summary>
internal static class DemoNames
{
    public static readonly string[] Owners = ["Helena Barbosa", "Rui Carvalho", "Sofia Teixeira", "Miguel Andrade", "Inês Valente"];

    private static readonly string[] FirstNames =
    [
        "Ana", "Bruno", "Carla", "Diogo", "Eva", "Filipe", "Gabriela", "Hugo", "Isabel", "João",
        "Kátia", "Luís", "Marta", "Nuno", "Olga", "Pedro", "Quitéria", "Rafael", "Sara", "Tiago",
        "Úrsula", "Vasco", "Xavier", "Yara", "Zé", "Beatriz", "Cláudio", "Daniela", "Eduardo", "Fátima",
    ];

    private static readonly string[] LastNames =
    [
        "Silva", "Santos", "Ferreira", "Pereira", "Oliveira", "Costa", "Rodrigues", "Martins", "Jesus", "Sousa",
        "Fernandes", "Gonçalves", "Gomes", "Lopes", "Marques", "Alves", "Almeida", "Ribeiro", "Pinto", "Carvalho",
        "Teixeira", "Moreira", "Correia", "Mendes",
    ];

    public static string OwnerEmail(int index) => $"dono{index + 1:00}@{DemoDataSeeder.EmailDomain}";

    public static string PlayerEmail(int index) => $"jogador{index + 1:00}@{DemoDataSeeder.EmailDomain}";

    public static string PlayerName(int index) => $"{FirstNames[index % FirstNames.Length]} {LastNames[((index * 7) + ((index / FirstNames.Length) * 3)) % LastNames.Length]}";

    public static string CustomerName(Random random) => $"{FirstNames[random.Next(FirstNames.Length)]} {LastNames[random.Next(LastNames.Length)]}";

    public static string CustomerPhone(Random random) => $"91{random.Next(1_000_000, 10_000_000)}";
}
