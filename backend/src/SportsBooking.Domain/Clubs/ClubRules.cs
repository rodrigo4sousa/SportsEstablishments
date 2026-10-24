using System.Globalization;
using System.Text;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.Domain.Clubs;

/// <summary>
/// US06 field limits for a club (provisional, see US06 in docs/backlog.md). They are also the column lengths.
/// Phone and contact email follow the account rules of US01 (<see cref="PhoneNumberPolicy"/>,
/// <see cref="EmailAddressPolicy"/>).
/// </summary>
public static class ClubRules
{
    public const int NameMaximumLength = 100;

    public const int AddressMaximumLength = 200;

    public const int LocalityMaximumLength = 100;

    /// <summary>Normalized phone: optional "+" and up to <see cref="PhoneNumberPolicy.MaximumDigits"/> digits.</summary>
    public const int PhoneMaximumLength = PhoneNumberPolicy.MaximumDigits + 1;

    public const int ContactEmailMaximumLength = EmailAddressPolicy.MaximumLength;

    public const int DefaultCancellationWindowHours = 24;

    /// <summary>At least one hour before the start; at most a week.</summary>
    public const int MinimumCancellationWindowHours = 1;

    public const int MaximumCancellationWindowHours = 168;

    public static bool IsValidCancellationWindow(int hours) =>
        hours is >= MinimumCancellationWindowHours and <= MaximumCancellationWindowHours;

    /// <summary>
    /// What the locality search compares (US16, provisional): the locality without accents, in lower case, with runs of
    /// white space reduced to one space ("São  João" and "sao joao" are the same). Stored in <c>clubs.locality_key</c>,
    /// like <c>pitches.name_key</c>, so the search is an indexed equality.
    /// </summary>
    public static string LocalityKey(string locality)
    {
        ArgumentNullException.ThrowIfNull(locality);
        var withoutAccents = new StringBuilder(locality.Length);
        foreach (var character in locality.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                withoutAccents.Append(character);
            }
        }

        var words = withoutAccents.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words);
    }
}
