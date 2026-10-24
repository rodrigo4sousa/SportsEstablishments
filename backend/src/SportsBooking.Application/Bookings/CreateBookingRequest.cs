using System.Globalization;
using System.Text.RegularExpressions;
using FluentValidation;

namespace SportsBooking.Application.Bookings;

/// <summary>
/// US18: a player books one slot. <see cref="StartsAt"/> is the slot's start as an instant with its zone, exactly as
/// the slot lists give it ("2026-10-25T00:00:00Z"), so the repeated October hour is never ambiguous. The end, the
/// price and the player never come from the request.
/// </summary>
public sealed record CreateBookingRequest(Guid PitchId, string StartsAt)
{
    /// <summary>The start as a UTC instant, or null if it is not an ISO 8601 instant with a zone.</summary>
    public DateTime? StartsAtUtc => SlotInstant.Parse(StartsAt);
}

/// <summary>The start of a slot as the slot lists give it: an ISO 8601 instant with its zone (US18, US13).</summary>
public static partial class SlotInstant
{
    /// <summary>The instant as UTC, or null if it is not a date, a time and a zone ("Z" or "+01:00").</summary>
    public static DateTime? Parse(string? text) =>
        text is not null && ZonedInstant().IsMatch(text.Trim())
        && DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
            ? instant.UtcDateTime
            : null;

    // Without the zone the instant would depend on the server.
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
