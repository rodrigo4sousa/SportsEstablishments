using FluentValidation;

namespace SportsBooking.Application.Auth;

/// <summary>
/// US03: only checks that both fields are filled in. Anything else (a malformed or unknown email, a wrong password)
/// is the generic <see cref="AuthErrors.InvalidCredentials"/>, so the answer never says whether the email exists.
/// </summary>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(request => request.Email).NotEmpty().WithMessage("Indica o teu email.");
        // Not NotEmpty(): that also rejects whitespace, and a password of spaces is a valid password (US01).
        RuleFor(request => request.Password).Must(password => password.Length > 0).WithMessage("Indica a palavra-passe.");
    }
}
