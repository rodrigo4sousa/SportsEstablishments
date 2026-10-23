using System.Globalization;
using System.Text;

namespace SportsBooking.Domain.Clubs;

/// <summary>
/// The club's address in public URLs, <c>/clubs/{slug}</c> (US06, US16). Made from the name when the club is
/// created and never changed afterwards, so public links keep working after a rename.
/// </summary>
public static class ClubSlug
{
    /// <summary>Used when a name has no letters or digits that survive (e.g. only symbols).</summary>
    public const string Fallback = "clube";

    /// <summary>Longest slug made from a name; a collision suffix ("-2") may follow.</summary>
    public const int BaseMaximumLength = 80;

    /// <summary>Column length: the base, a hyphen and a suffix of up to ten digits.</summary>
    public const int MaximumLength = BaseMaximumLength + 11;

    /// <summary>
    /// Lowercase ASCII letters and digits, accents removed, every other run of characters becoming one hyphen:
    /// "Clube de Ténis da Foz" → "clube-de-tenis-da-foz", "Padel &amp; Co." → "padel-co".
    /// </summary>
    public static string FromName(string name)
    {
        var builder = new StringBuilder(name.Length);
        var pendingHyphen = false;
        foreach (var character in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                // The accent of a letter just written ("é" is "e" plus a combining mark).
                continue;
            }

            var lower = char.ToLowerInvariant(character);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingHyphen && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(lower);
                pendingHyphen = false;
            }
            else
            {
                pendingHyphen = true;
            }
        }

        var slug = Truncate(builder.ToString());
        return slug.Length == 0 ? Fallback : slug;
    }

    /// <summary>The slug to use when <paramref name="baseSlug"/> is taken: "-2" for the first collision, then "-3", …</summary>
    public static string WithSuffix(string baseSlug, int suffix) =>
        suffix < 2
            ? throw new ArgumentOutOfRangeException(nameof(suffix), suffix, "Collision suffixes start at 2.")
            : $"{baseSlug}-{suffix.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// The first free slug for <paramref name="baseSlug"/> given the slugs already <paramref name="taken"/>: the base
    /// if it is free, otherwise the next suffix after the highest one in use ("-2" for the first collision).
    /// </summary>
    public static string NextAvailable(string baseSlug, IEnumerable<string> taken)
    {
        var suffixes = taken.Select(slug => SuffixOf(slug, baseSlug)).OfType<int>().ToList();
        return suffixes.Contains(1) ? WithSuffix(baseSlug, Math.Max(suffixes.Max(), 1) + 1) : baseSlug;
    }

    /// <summary>
    /// The collision suffix of <paramref name="slug"/> for <paramref name="baseSlug"/>: 1 for the base itself,
    /// the number for "base-N", and null for any other slug (e.g. "base-club" or "base-2-2").
    /// </summary>
    public static int? SuffixOf(string slug, string baseSlug)
    {
        if (slug == baseSlug)
        {
            return 1;
        }

        if (!slug.StartsWith(baseSlug + "-", StringComparison.Ordinal))
        {
            return null;
        }

        var rest = slug.AsSpan(baseSlug.Length + 1);
        // "base-1" or "base-007" never come from WithSuffix; they are other names, not collisions.
        return rest.Length is > 0 and <= 9 && rest[0] != '0' && int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out var suffix) && suffix >= 2
            ? suffix
            : null;
    }

    // Long names are cut at a word boundary where possible, never leaving a trailing hyphen.
    private static string Truncate(string slug)
    {
        if (slug.Length <= BaseMaximumLength)
        {
            return slug;
        }

        var cut = slug[..BaseMaximumLength];
        var lastHyphen = cut.LastIndexOf('-');
        return (lastHyphen > 0 ? cut[..lastHyphen] : cut).TrimEnd('-');
    }
}
