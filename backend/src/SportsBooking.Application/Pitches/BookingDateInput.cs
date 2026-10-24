using System.Globalization;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Application.Pitches;

/// <summary>
/// A date a player or an owner asks for ("yyyy-MM-dd", a local date): the same checks and messages for the owner's
/// slot preview (US08), the club search (US16) and the free slots (US17). Only today up to 30 days ahead (US17).
/// </summary>
public static class BookingDateInput
{
    public const string Format = "yyyy-MM-dd";

    /// <summary>The date, or a 400 on the <c>date</c> field. <paramref name="today"/> is today in the club's time zone.</summary>
    public static Result<DateOnly> Parse(string? date, DateOnly today)
    {
        if (!DateOnly.TryParseExact(date, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return DateError("Indica a data no formato AAAA-MM-DD, por exemplo 2026-10-25.");
        }

        return BookingWindow.Contains(day, today)
            ? day
            : DateError($"Escolhe uma data de hoje até {BookingWindow.DaysAhead} dias à frente.");
    }

    private static ServiceError DateError(string message) =>
        ServiceError.Validation(new Dictionary<string, string[]> { ["date"] = [message] });
}
