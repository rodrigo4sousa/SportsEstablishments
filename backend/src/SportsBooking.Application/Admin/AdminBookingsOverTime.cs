using System.Globalization;
using FluentValidation;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Application.Admin;

public enum Granularity
{
    Day,
    Week,
    Month,
}

/// <summary>The periods of the admin's charts (US25, US28): the Lisbon calendar day, the week that starts on Monday, the month.</summary>
public static class ReportingPeriods
{
    public static string ToCode(Granularity granularity) => granularity switch
    {
        Granularity.Day => "day",
        Granularity.Week => "week",
        Granularity.Month => "month",
        _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, "Unknown granularity."),
    };

    public static bool TryParse(string? code, out Granularity granularity)
    {
        granularity = code switch { "day" => Granularity.Day, "week" => Granularity.Week, "month" => Granularity.Month, _ => default };
        return code is "day" or "week" or "month";
    }

    /// <summary>The most periods one chart has (provisional): a year of days, two years of weeks, five years of months.</summary>
    public static int MaximumPeriods(Granularity granularity) => granularity switch
    {
        Granularity.Day => 366,
        Granularity.Week => 104,
        _ => 60,
    };

    /// <summary>The first day of the period a date is in: the date itself, its Monday, or the first of its month.</summary>
    public static DateOnly Start(DateOnly date, Granularity granularity) => granularity switch
    {
        Granularity.Day => date,
        Granularity.Week => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
        _ => new DateOnly(date.Year, date.Month, 1),
    };

    /// <summary>The first day of the period after the one that starts at <paramref name="start"/>.</summary>
    public static DateOnly Next(DateOnly start, Granularity granularity) => granularity switch
    {
        Granularity.Day => start.AddDays(1),
        Granularity.Week => start.AddDays(7),
        _ => start.AddMonths(1),
    };

    /// <summary>The last day of the period that starts at <paramref name="start"/>.</summary>
    public static DateOnly End(DateOnly start, Granularity granularity) => Next(start, granularity).AddDays(-1);
}

/// <summary>Bookings counted in one period: <see cref="Start"/> is its first day, in Lisbon time.</summary>
public sealed record PeriodCount(DateOnly Start, int Count);

/// <summary>
/// The grouped count behind the chart, implemented with PostgreSQL's <c>date_trunc</c> in Infrastructure: bookings created
/// in <c>[fromUtc, toUtc)</c>, grouped by the Lisbon period their creation falls in. Periods with no bookings are not returned.
/// </summary>
public interface IBookingStatistics
{
    Task<IReadOnlyList<PeriodCount>> CountCreatedByPeriodAsync(
        Granularity granularity,
        DateTime fromUtc,
        DateTime toUtc,
        bool includeCancelled,
        CancellationToken cancellationToken);
}

/// <summary>The query string of <c>GET /api/admin/bookings-over-time</c>, as text, so a wrong value gets a message in Portuguese.</summary>
public sealed record BookingsOverTimeRequest(string? Granularity, string? From, string? To, string? IncludeCancelled)
{
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>The first and last date accepted: a sanity bound, so no date arithmetic can overflow.</summary>
    public static readonly DateOnly Earliest = new(2000, 1, 1);

    public static readonly DateOnly Latest = new(2100, 12, 31);

    /// <summary>The default range: today and the 29 days before it, 30 days (provisional).</summary>
    public const int DefaultDays = 30;

    public BookingsOverTimeRequest Normalized() => this with
    {
        Granularity = string.IsNullOrWhiteSpace(Granularity) ? null : Granularity.Trim().ToLowerInvariant(),
        From = string.IsNullOrWhiteSpace(From) ? null : From.Trim(),
        To = string.IsNullOrWhiteSpace(To) ? null : To.Trim(),
        IncludeCancelled = string.IsNullOrWhiteSpace(IncludeCancelled) ? null : IncludeCancelled.Trim().ToLowerInvariant(),
    };

    public static bool TryParseDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}

public sealed class BookingsOverTimeRequestValidator : AbstractValidator<BookingsOverTimeRequest>
{
    private const string BadDate = "Indica a data no formato AAAA-MM-DD, por exemplo 2026-10-25.";

    public BookingsOverTimeRequestValidator()
    {
        RuleFor(request => request.Granularity)
            .Must(value => value is null || ReportingPeriods.TryParse(value, out _))
            .WithMessage("Indica a granularidade: «day», «week» ou «month».");

        RuleFor(request => request.From)
            .Cascade(CascadeMode.Stop)
            .Must(value => value is null || BookingsOverTimeRequest.TryParseDate(value, out _)).WithMessage(BadDate)
            .Must(InBounds).WithMessage($"A data tem de estar entre {BookingsOverTimeRequest.Earliest.Year} e {BookingsOverTimeRequest.Latest.Year}.");

        RuleFor(request => request.To)
            .Cascade(CascadeMode.Stop)
            .Must(value => value is null || BookingsOverTimeRequest.TryParseDate(value, out _)).WithMessage(BadDate)
            .Must(InBounds).WithMessage($"A data tem de estar entre {BookingsOverTimeRequest.Earliest.Year} e {BookingsOverTimeRequest.Latest.Year}.")
            .Must((request, _) => !BothDates(request, out var from, out var to) || from <= to)
            .WithMessage("A data final não pode ser anterior à inicial.");

        RuleFor(request => request.IncludeCancelled)
            .Must(value => value is null or "true" or "false")
            .WithMessage("Indica «true» ou «false».");
    }

    private static bool InBounds(string? value) =>
        value is null || (BookingsOverTimeRequest.TryParseDate(value, out var date) && date >= BookingsOverTimeRequest.Earliest && date <= BookingsOverTimeRequest.Latest);

    private static bool BothDates(BookingsOverTimeRequest request, out DateOnly from, out DateOnly to)
    {
        from = default;
        to = default;
        return request.From is not null && request.To is not null
            && BookingsOverTimeRequest.TryParseDate(request.From, out from) && BookingsOverTimeRequest.TryParseDate(request.To, out to);
    }
}

/// <summary>One period of the chart: its first and last day (Lisbon dates, "yyyy-MM-dd") and how many bookings.</summary>
public sealed record PeriodBucket(string Start, string End, int Count);

/// <summary>
/// The bookings of the chart, period by period. <see cref="From"/> and <see cref="To"/> are the whole periods the asked range
/// was widened to, so no period is partial.
/// </summary>
public sealed record BookingsOverTimeResponse(string Granularity, string From, string To, bool IncludeCancelled, int Total, IReadOnlyList<PeriodBucket> Buckets);

/// <summary>
/// US25: bookings over time. A booking counts in the period it was <b>created</b> in (provisional: when it was made, not when
/// it is played), by the Lisbon calendar. The range is widened to whole periods (a week from Monday to Sunday, a month from
/// the 1st), every period in it is present, with 0 where nothing was booked, and there are at most
/// <see cref="ReportingPeriods.MaximumPeriods"/> of them. Cancelled bookings (by the player or by the club) are left out unless
/// asked for; a no-show is not a cancellation, and bookings the club made for a customer (US13) count, as bookings of the platform
/// (provisional). Counted by the database; the zeros are filled in here.
/// </summary>
public sealed class AdminBookingsOverTimeService(IBookingStatistics statistics, BookingsOverTimeRequestValidator validator, TimeProvider timeProvider)
{
    private static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById(ClubTimeZones.LisbonId);

    public async Task<Result<BookingsOverTimeResponse>> GetAsync(BookingsOverTimeRequest request, CancellationToken cancellationToken)
    {
        var normalized = request.Normalized();
        var validation = await validator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        var granularity = normalized.Granularity is { } code && ReportingPeriods.TryParse(code, out var parsed) ? parsed : Granularity.Day;
        var today = ClubTime.Today(timeProvider, Lisbon);
        var to = today;
        if (normalized.To is not null)
        {
            to = DateOnly.ParseExact(normalized.To, BookingsOverTimeRequest.DateFormat, CultureInfo.InvariantCulture);
        }

        var from = to.AddDays(-(BookingsOverTimeRequest.DefaultDays - 1));
        if (normalized.From is not null)
        {
            from = DateOnly.ParseExact(normalized.From, BookingsOverTimeRequest.DateFormat, CultureInfo.InvariantCulture);
        }

        if (from > to)
        {
            return RangeError("from", "A data inicial não pode ser posterior à final.");
        }

        // Whole periods: the one `from` is in, up to the one `to` is in.
        var firstStart = ReportingPeriods.Start(from, granularity);
        var lastStart = ReportingPeriods.Start(to, granularity);
        var starts = new List<DateOnly>();
        for (var start = firstStart; start <= lastStart; start = ReportingPeriods.Next(start, granularity))
        {
            starts.Add(start);
            if (starts.Count > ReportingPeriods.MaximumPeriods(granularity))
            {
                var unit = granularity switch { Granularity.Day => "dias", Granularity.Week => "semanas", _ => "meses" };
                return RangeError("to", $"O intervalo tem demasiados períodos: no máximo {ReportingPeriods.MaximumPeriods(granularity)} {unit}.");
            }
        }

        var effectiveTo = ReportingPeriods.End(lastStart, granularity);
        var fromUtc = ClubTime.ToUtc(firstStart, TimeOnly.MinValue, Lisbon);
        var toUtc = ClubTime.ToUtc(effectiveTo.AddDays(1), TimeOnly.MinValue, Lisbon);
        var includeCancelled = normalized.IncludeCancelled == "true";

        var counted = (await statistics.CountCreatedByPeriodAsync(granularity, fromUtc, toUtc, includeCancelled, cancellationToken))
            .ToDictionary(row => row.Start, row => row.Count);
        var buckets = starts
            .Select(start => new PeriodBucket(
                Format(start),
                Format(ReportingPeriods.End(start, granularity)),
                counted.GetValueOrDefault(start)))
            .ToList();

        return new BookingsOverTimeResponse(
            ReportingPeriods.ToCode(granularity), Format(firstStart), Format(effectiveTo), includeCancelled, buckets.Sum(bucket => bucket.Count), buckets);
    }

    private static string Format(DateOnly date) => date.ToString(BookingsOverTimeRequest.DateFormat, CultureInfo.InvariantCulture);

    private static ServiceError RangeError(string field, string message) =>
        ServiceError.Validation(new Dictionary<string, string[]> { [field] = [message] });
}
