using FluentValidation;
using SportsBooking.Domain.Pitches;

namespace SportsBooking.Application.Pitches;

/// <summary>
/// US07 create and edit form. The club comes from the route, the status from the deactivate and reactivate
/// actions. <see cref="Sport"/> is one of the stable codes (futsal, football7, padel, tennis).
/// </summary>
public sealed record PitchRequest(string Name, string Sport, decimal PricePerSlot)
{
    /// <summary>Trims the name and the sport code and treats missing values as empty.</summary>
    public PitchRequest Normalized() => this with
    {
        Name = Name?.Trim() ?? string.Empty,
        Sport = Sport?.Trim() ?? string.Empty,
    };

    /// <summary>Only for a request that passed validation: the sport code is known then.</summary>
    public PitchDetails Details()
    {
        return Sports.TryParse(Sport, out var sport)
            ? new PitchDetails(Name, sport, PricePerSlot)
            : throw new InvalidOperationException("The sport code was not validated.");
    }
}

/// <summary>US07 field rules, applied to a <see cref="PitchRequest.Normalized"/> request. Messages are pt-PT.</summary>
public sealed class PitchRequestValidator : AbstractValidator<PitchRequest>
{
    public PitchRequestValidator()
    {
        RuleFor(request => request.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica o nome do campo.")
            .MaximumLength(PitchRules.NameMaximumLength)
            .WithMessage($"O nome pode ter no máximo {PitchRules.NameMaximumLength} caracteres.");

        RuleFor(request => request.Sport)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica o desporto do campo.")
            .Must(code => Sports.TryParse(code, out _))
            .WithMessage("Escolhe um desporto da lista: futsal, futebol 7, padel ou ténis.");

        RuleFor(request => request.PricePerSlot)
            .Cascade(CascadeMode.Stop)
            .Must(PitchRules.IsPositive).WithMessage("O preço por slot tem de ser maior que 0 €.")
            .Must(PitchRules.IsWithinMaximum).WithMessage($"O preço por slot não pode passar de {PitchRules.MaximumPrice:0} €.")
            .Must(PitchRules.HasAllowedDecimals).WithMessage("O preço por slot pode ter no máximo duas casas decimais.");
    }
}
