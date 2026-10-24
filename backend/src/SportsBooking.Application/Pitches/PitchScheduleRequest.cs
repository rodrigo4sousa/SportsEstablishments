using System.Globalization;
using FluentValidation;
using SportsBooking.Domain.Pitches;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Application.Pitches;

/// <summary>
/// One weekday of a schedule (US08): <c>{ "closed": true }</c>, or open from <see cref="Opens"/> to
/// <see cref="Closes"/> as local "HH:mm" times in the club's time zone. A closing time of "00:00" is midnight.
/// </summary>
public sealed record DayHours(bool Closed, string? Opens, string? Closes)
{
    public const string TimeFormat = "HH:mm";

    public static DayHours ClosedDay { get; } = new(true, null, null);

    public static DayHours From(OpeningHours? hours) =>
        hours is null ? ClosedDay : new DayHours(false, Format(hours.Opens), Format(hours.Closes));

    public static bool TryParseTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    /// <summary>Only for an open day that passed validation.</summary>
    public OpeningHours ToOpeningHours() =>
        TryParseTime(Opens, out var opens) && TryParseTime(Closes, out var closes)
            ? OpeningHours.Create(opens, closes)
            : throw new InvalidOperationException("The hours were not validated.");

    private static string Format(TimeOnly time) => time.ToString(TimeFormat, CultureInfo.InvariantCulture);
}

/// <summary>
/// US08: a pitch's slot length and its whole week. <c>PUT</c> replaces everything, so every weekday is required
/// (closed or with hours). The same shape comes back from <c>GET</c>.
/// </summary>
public sealed record PitchScheduleRequest(
    int SlotDurationMinutes,
    DayHours Monday,
    DayHours Tuesday,
    DayHours Wednesday,
    DayHours Thursday,
    DayHours Friday,
    DayHours Saturday,
    DayHours Sunday)
{
    public DayHours? On(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => Monday,
        DayOfWeek.Tuesday => Tuesday,
        DayOfWeek.Wednesday => Wednesday,
        DayOfWeek.Thursday => Thursday,
        DayOfWeek.Friday => Friday,
        DayOfWeek.Saturday => Saturday,
        DayOfWeek.Sunday => Sunday,
        _ => throw new ArgumentOutOfRangeException(nameof(day), day, "Unknown weekday."),
    };

    /// <summary>Only for a request that passed validation.</summary>
    public IReadOnlyDictionary<DayOfWeek, OpeningHours?> Week() =>
        PitchScheduleRules.Week.ToDictionary(day => day, day => On(day)!.Closed ? null : On(day)!.ToOpeningHours());
}

/// <summary>US08 field rules. Messages are pt-PT; the keys are the JSON paths (<c>monday.opens</c>).</summary>
public sealed class PitchScheduleRequestValidator : AbstractValidator<PitchScheduleRequest>
{
    private const string InvalidTime = "Indica a hora no formato HH:mm, por exemplo 09:00.";
    private const string OffStep = "As horas vão de meia em meia hora, por exemplo 09:00 ou 09:30.";
    private const string NotAfterOpening =
        "A hora de fecho tem de ser depois da abertura. O fecho mais tarde é 00:00 (meia-noite); horários que passam da meia-noite não são aceites.";

    private static readonly Dictionary<DayOfWeek, (string Key, string Name)> Days = new()
    {
        [DayOfWeek.Monday] = ("monday", "segunda-feira"),
        [DayOfWeek.Tuesday] = ("tuesday", "terça-feira"),
        [DayOfWeek.Wednesday] = ("wednesday", "quarta-feira"),
        [DayOfWeek.Thursday] = ("thursday", "quinta-feira"),
        [DayOfWeek.Friday] = ("friday", "sexta-feira"),
        [DayOfWeek.Saturday] = ("saturday", "sábado"),
        [DayOfWeek.Sunday] = ("sunday", "domingo"),
    };

    public PitchScheduleRequestValidator()
    {
        RuleFor(request => request.SlotDurationMinutes)
            .Must(SlotDuration.IsSupported)
            .WithMessage("A duração do slot tem de ser 30, 60 ou 90 minutos.");

        foreach (var day in PitchScheduleRules.Week)
        {
            var (key, name) = Days[day];

            RuleFor(request => request.On(day))
                .NotNull()
                .WithMessage($"Indica o horário de {name} ou marca o dia como fechado.")
                .OverridePropertyName(key);

            RuleFor(request => request.On(day)!)
                .Must(hours => string.IsNullOrEmpty(hours.Opens) && string.IsNullOrEmpty(hours.Closes))
                .When(request => request.On(day) is { Closed: true })
                .WithMessage($"Um dia fechado não tem horas: tira as horas de {name} ou abre o dia.")
                .OverridePropertyName(key);

            When(request => request.On(day) is { Closed: false }, () =>
            {
                RuleFor(request => request.On(day)!.Opens)
                    .Cascade(CascadeMode.Stop)
                    .NotEmpty().WithMessage($"Indica a hora de abertura de {name}.")
                    .Must(text => DayHours.TryParseTime(text, out _)).WithMessage(InvalidTime)
                    .Must(text => IsOnStep(text)).WithMessage(OffStep)
                    .OverridePropertyName($"{key}.opens");

                RuleFor(request => request.On(day)!.Closes)
                    .Cascade(CascadeMode.Stop)
                    .NotEmpty().WithMessage($"Indica a hora de fecho de {name}.")
                    .Must(text => DayHours.TryParseTime(text, out _)).WithMessage(InvalidTime)
                    .Must(text => IsOnStep(text)).WithMessage(OffStep)
                    .OverridePropertyName($"{key}.closes");

                // Only once both times are readable and on the step: one message per field.
                RuleFor(request => request.On(day)!)
                    .Must(hours => ClosesAfterOpening(hours))
                    .When(request => HasValidTimes(request.On(day)!))
                    .WithMessage(NotAfterOpening)
                    .OverridePropertyName($"{key}.closes");
            });
        }
    }

    private static bool IsOnStep(string? text) => DayHours.TryParseTime(text, out var time) && PitchScheduleRules.IsOnStep(time);

    private static bool HasValidTimes(DayHours hours) => IsOnStep(hours.Opens) && IsOnStep(hours.Closes);

    private static bool ClosesAfterOpening(DayHours hours) =>
        DayHours.TryParseTime(hours.Opens, out var opens)
        && DayHours.TryParseTime(hours.Closes, out var closes)
        && OpeningHours.AreValid(opens, closes);
}
