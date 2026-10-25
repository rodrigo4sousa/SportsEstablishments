using FluentValidation;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Clubs;

namespace SportsBooking.Application.Clubs;

/// <summary>
/// US06 create and edit form. The owner, the slug and the time zone are never part of it: the owner comes from the
/// session, the slug is made from the name at creation, and the time zone is always Europe/Lisbon (T04).
/// </summary>
/// <param name="CancellationWindowHours">Optional when creating (24 by default); required when editing.</param>
/// <param name="NotifyOnNewBooking">US23: the owner's email about new bookings. Optional both ways: on when creating, and left as it is when editing.</param>
public sealed record ClubRequest(
    string Name,
    string Address,
    string Locality,
    string Phone,
    string ContactEmail,
    int? CancellationWindowHours = null,
    bool? NotifyOnNewBooking = null)
{
    /// <summary>Trims every text, removes spaces from the phone and treats missing values as empty.</summary>
    public ClubRequest Normalized() => this with
    {
        Name = Name?.Trim() ?? string.Empty,
        Address = Address?.Trim() ?? string.Empty,
        Locality = Locality?.Trim() ?? string.Empty,
        Phone = PhoneNumberPolicy.Normalize(Phone) ?? string.Empty,
        ContactEmail = ContactEmail?.Trim() ?? string.Empty,
    };

    public ClubDetails Details() => new(Name, Address, Locality, Phone, ContactEmail);
}

/// <summary>US06 field rules, applied to a <see cref="ClubRequest.Normalized"/> request. Messages are pt-PT.</summary>
public abstract class ClubRequestValidator : AbstractValidator<ClubRequest>
{
    private static readonly string WindowRange =
        $"A janela de cancelamento tem de estar entre {ClubRules.MinimumCancellationWindowHours} e {ClubRules.MaximumCancellationWindowHours} horas.";

    protected ClubRequestValidator(bool cancellationWindowRequired)
    {
        RuleFor(request => request.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica o nome do clube.")
            .MaximumLength(ClubRules.NameMaximumLength)
            .WithMessage($"O nome pode ter no máximo {ClubRules.NameMaximumLength} caracteres.");

        RuleFor(request => request.Address)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica a morada do clube.")
            .MaximumLength(ClubRules.AddressMaximumLength)
            .WithMessage($"A morada pode ter no máximo {ClubRules.AddressMaximumLength} caracteres.");

        RuleFor(request => request.Locality)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica a localidade do clube.")
            .MaximumLength(ClubRules.LocalityMaximumLength)
            .WithMessage($"A localidade pode ter no máximo {ClubRules.LocalityMaximumLength} caracteres.");

        RuleFor(request => request.Phone)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica o telefone do clube.")
            .Must(PhoneNumberPolicy.IsValid)
            .WithMessage($"Indica um número de telefone válido, com {PhoneNumberPolicy.MinimumDigits} a {PhoneNumberPolicy.MaximumDigits} algarismos.");

        RuleFor(request => request.ContactEmail)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica o email de contacto do clube.")
            .Must(EmailAddressPolicy.IsValid).WithMessage("Indica um email válido, por exemplo nome@exemplo.pt.");

        if (cancellationWindowRequired)
        {
            RuleFor(request => request.CancellationWindowHours)
                .NotNull().WithMessage("Indica a janela de cancelamento, em horas.");
        }

        RuleFor(request => request.CancellationWindowHours)
            .Must(hours => ClubRules.IsValidCancellationWindow(hours!.Value))
            .When(request => request.CancellationWindowHours is not null)
            .WithMessage(WindowRange);
    }
}

public sealed class CreateClubRequestValidator() : ClubRequestValidator(cancellationWindowRequired: false);

public sealed class UpdateClubRequestValidator() : ClubRequestValidator(cancellationWindowRequired: true);
