using System.Globalization;
using FluentValidation;
using SportsBooking.Domain.Pitches;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Application.Pitches;

/// <summary>
/// US09: a block as the owner types it. <see cref="Start"/> and <see cref="End"/> are club local date-times
/// ("2026-10-25T09:00"), converted with <see cref="ClubTime"/>; the API answers in UTC instants.
/// </summary>
public sealed record BlockRequest(string Start, string End, string? Reason = null)
{
    public const string LocalFormat = "yyyy-MM-dd'T'HH:mm";

    public BlockRequest Normalized() => this with
    {
        Start = Start?.Trim() ?? string.Empty,
        End = End?.Trim() ?? string.Empty,
        Reason = string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim(),
    };
}

/// <summary>A normalized request with what validation needs: the current instant and the club's time zone.</summary>
public sealed record BlockCheck(BlockRequest Request, DateTime Now, TimeZoneInfo TimeZone)
{
    public static bool TryParseLocal(string? text, out DateOnly date, out TimeOnly time)
    {
        date = default;
        time = default;
        if (!DateTime.TryParseExact(text, BlockRequest.LocalFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        date = DateOnly.FromDateTime(parsed);
        time = TimeOnly.FromDateTime(parsed);
        return true;
    }

    /// <summary>The local text as a UTC instant by the DST rules of <see cref="ClubTime.ToUtc"/>, or null if it is not a valid date-time.</summary>
    public DateTime? ToUtc(string? text) => TryParseLocal(text, out var date, out var time) ? ClubTime.ToUtc(date, time, TimeZone) : null;

    public DateTime? StartUtc => ToUtc(Request.Start);

    public DateTime? EndUtc => ToUtc(Request.End);
}

/// <summary>US09 field rules, applied to a <see cref="BlockCheck"/>. Messages are pt-PT; keys are <c>start</c>, <c>end</c>, <c>reason</c>.</summary>
public sealed class BlockCheckValidator : AbstractValidator<BlockCheck>
{
    private const string InvalidDateTime = "Indica a data e a hora no formato AAAA-MM-DDTHH:mm, por exemplo 2026-10-25T09:00.";
    private static readonly string OffStep = $"As horas vão de meia em meia hora, por exemplo 09:00 ou 09:30.";

    public BlockCheckValidator()
    {
        RuleFor(check => check.Request.Start)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica o início do bloqueio.")
            .Must(text => BlockCheck.TryParseLocal(text, out _, out _)).WithMessage(InvalidDateTime)
            .Must(OnStep).WithMessage(OffStep)
            .Must((check, _) => check.StartUtc >= check.Now).WithMessage("O início não pode estar no passado.")
            .OverridePropertyName("start");

        RuleFor(check => check.Request.End)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica o fim do bloqueio.")
            .Must(text => BlockCheck.TryParseLocal(text, out _, out _)).WithMessage(InvalidDateTime)
            .Must(OnStep).WithMessage(OffStep)
            .Must((check, _) => check.StartUtc is null || check.EndUtc > check.StartUtc).WithMessage("O fim tem de ser depois do início.")
            .Must((check, _) => check.EndUtc <= check.Now.AddDays(BlockRules.MaximumDaysAhead))
            .WithMessage($"O bloqueio não pode acabar daqui a mais de {BlockRules.MaximumDaysAhead} dias.")
            .OverridePropertyName("end");

        RuleFor(check => check.Request.Reason)
            .MaximumLength(BlockRules.ReasonMaximumLength)
            .WithMessage($"O motivo pode ter no máximo {BlockRules.ReasonMaximumLength} caracteres.")
            .OverridePropertyName("reason");
    }

    private static bool OnStep(string? text) =>
        BlockCheck.TryParseLocal(text, out _, out var time) && time.Minute % BlockRules.TimeStepMinutes == 0;
}
