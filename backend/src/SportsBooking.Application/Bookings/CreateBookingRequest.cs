using System.Globalization;
using System.Text.RegularExpressions;
using FluentValidation;

namespace SportsBooking.Application.Bookings;

/// <summary>
/// US18: a player books one slot. <see cref="StartsAt"/> is the slot's start as an instant with its zone, exactly as
/// the slot lists give it ("2026-10-25T00:00:00Z"), so the repeated October hour is never ambiguous. The end, the
/// price and the player never come from the request.
/// </summary>
public sealed partial record CreateBookingRequest(Guid PitchId, string StartsAt)
{
    /// <summary>The start as a UTC instant, or null if it is not an ISO 8601 instant with a zone.</summary>
    public DateTime? StartsAtUtc =>
        StartsAt is not null && ZonedInstant().IsMatch(StartsAt.Trim())
        && DateTimeOffset.TryParse(StartsAt.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
            ? instant.UtcDateTime
            : null;

    // A date, a time and a zone ("Z" or "+01:00"): without the zone the instant would depend on the server.
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex ZonedInstant();
}

/// <summary>US18 field rules. Messages are pt-PT; keys are <c>pitchId</c> and <c>startsAt</c>.</summary>
public sealed class CreateBookingRequestValidator : AbstractValidator<CreateBookingRequest>
{
    public CreateBookingRequestValidator()
    {
        RuleFor(request => request.PitchId)
            .NotEmpty().WithMessage("Indica o campo a reservar.");

        RuleFor(request => request.StartsAt)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica o início do slot.")
            .Must((request, _) => request.StartsAtUtc is not null)
            .WithMessage("Indica o início do slot como instante com fuso, por exemplo 2026-10-25T09:00:00Z.");
    }
}
