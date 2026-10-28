using System.Globalization;
using FluentValidation;
using SportsBooking.Domain.Pitches;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Application.Pitches;

/// <summary>
/// US10: a price band as the owner types it. <see cref="Days"/> are weekday names in English lower case ("monday" …
/// "sunday", like the schedule's keys); <see cref="Start"/> and <see cref="End"/> are local "HH:mm" times of the club in
/// 30-minute steps, "00:00" as the end meaning midnight; <see cref="Price"/> is euros per slot.
/// </summary>
public sealed record PriceBandRequest(IReadOnlyList<string> Days, string Start, string End, decimal Price)
{
    public static readonly IReadOnlyDictionary<string, DayOfWeek> DaysByName = PitchScheduleRules.Week.ToDictionary(NameOf);

    public static string NameOf(DayOfWeek day) => day.ToString().ToLowerInvariant();

    public static bool TryParseDay(string? name, out DayOfWeek day) => DaysByName.TryGetValue(name ?? string.Empty, out day);

    /// <summary>Only for a request that passed validation.</summary>
    public PriceBandDetails ToDetails() => new(
        Days.Select(name => DaysByName[name]).ToHashSet(),
        DayHours.TryParseTime(Start, out var start) ? start : throw new InvalidOperationException("The band was not validated."),
        DayHours.TryParseTime(End, out var end) ? end : throw new InvalidOperationException("The band was not validated."),
        Price);

    /// <summary>The same band with the text trimmed and the day names in lower case, in week order, without repeats.</summary>
    public PriceBandRequest Normalized() => this with
    {
        Days = (Days ?? [])
            .Select(name => (name ?? string.Empty).Trim().ToLowerInvariant())
            .Distinct()
            .OrderBy(name => TryParseDay(name, out var day) ? PitchScheduleRules.Week.ToList().IndexOf(day) : int.MaxValue)
            .ToList(),
        Start = Start?.Trim() ?? string.Empty,
        End = End?.Trim() ?? string.Empty,
    };
}

/// <summary>A price band as its owner sees it, in the shape of <see cref="PriceBandRequest"/>.</summary>
public sealed record PriceBandResponse(Guid Id, Guid PitchId, IReadOnlyList<string> Days, string Start, string End, decimal Price)
{
    public static PriceBandResponse From(PitchPriceBand band) => new(
        band.Id,
        band.PitchId,
        PitchScheduleRules.Week.Where(band.Days.Contains).Select(PriceBandRequest.NameOf).ToList(),
        band.Starts.ToString(DayHours.TimeFormat, CultureInfo.InvariantCulture),
        band.Ends.ToString(DayHours.TimeFormat, CultureInfo.InvariantCulture),
        band.Price);
}

/// <summary>US10 field rules; messages are pt-PT, keys are <c>days</c>, <c>start</c>, <c>end</c> and <c>price</c>. Run on a normalized request.</summary>
public sealed class PriceBandRequestValidator : AbstractValidator<PriceBandRequest>
{
    private const string InvalidTime = "Indica a hora no formato HH:mm, por exemplo 18:00.";
    private const string OffStep = "As horas vão de meia em meia hora, por exemplo 18:00 ou 18:30.";

    public PriceBandRequestValidator()
    {
        RuleFor(request => request.Days)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Escolhe pelo menos um dia da semana.")
            .Must(days => days.All(name => PriceBandRequest.TryParseDay(name, out _)))
            .WithMessage("Os dias têm de ser monday, tuesday, wednesday, thursday, friday, saturday ou sunday.");

        RuleFor(request => request.Start)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica a hora de início da faixa.")
            .Must(text => DayHours.TryParseTime(text, out _)).WithMessage(InvalidTime)
            .Must(IsOnStep).WithMessage(OffStep);

        RuleFor(request => request.End)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica a hora de fim da faixa.")
            .Must(text => DayHours.TryParseTime(text, out _)).WithMessage(InvalidTime)
            .Must(IsOnStep).WithMessage(OffStep)
            .Must((request, _) => !IsOnStep(request.Start) || EndsAfterStart(request))
            .WithMessage("O fim tem de ser depois do início. O fim mais tarde é 00:00 (meia-noite); faixas que passam da meia-noite não são aceites.");

        RuleFor(request => request.Price)
            .Cascade(CascadeMode.Stop)
            .Must(PitchRules.IsPositive).WithMessage("O preço tem de ser maior que 0 €.")
            .Must(PitchRules.IsWithinMaximum).WithMessage($"O preço não pode passar de {PitchRules.MaximumPrice:0} €.")
            .Must(PitchRules.HasAllowedDecimals).WithMessage("O preço pode ter no máximo duas casas decimais.");
    }

    private static bool IsOnStep(string? text) => DayHours.TryParseTime(text, out var time) && PitchScheduleRules.IsOnStep(time);

    private static bool EndsAfterStart(PriceBandRequest request) =>
        DayHours.TryParseTime(request.Start, out var start) && DayHours.TryParseTime(request.End, out var end) && OpeningHours.AreValid(start, end);
}
