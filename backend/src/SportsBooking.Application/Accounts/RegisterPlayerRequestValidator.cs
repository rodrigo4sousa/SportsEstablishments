using FluentValidation;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.Application.Accounts;

/// <summary>US01 field rules, applied to a <see cref="RegisterPlayerRequest.Normalized"/> request. Messages are pt-PT.</summary>
public sealed class RegisterPlayerRequestValidator : AbstractValidator<RegisterPlayerRequest>
{
    public RegisterPlayerRequestValidator()
    {
        RuleFor(request => request.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica o teu nome.")
            .MaximumLength(AccountNamePolicy.MaximumLength)
            .WithMessage($"O nome pode ter no máximo {AccountNamePolicy.MaximumLength} caracteres.");

        RuleFor(request => request.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica o teu email.")
            .Must(EmailAddressPolicy.IsValid).WithMessage("Indica um email válido, por exemplo nome@exemplo.pt.");

        RuleFor(request => request.Password)
            .Must(PasswordPolicy.IsSatisfiedBy)
            .WithMessage($"A palavra-passe tem de ter pelo menos {PasswordPolicy.MinimumLength} caracteres.");

        RuleFor(request => request.Phone)
            .Must(PhoneNumberPolicy.IsValid)
            .When(request => request.Phone is not null)
            .WithMessage(
                $"Indica um número de telemóvel válido, com {PhoneNumberPolicy.MinimumDigits} a {PhoneNumberPolicy.MaximumDigits} algarismos.");
    }
}
