import { intlLocale, type AppLocale } from "@/i18n/locales";

function cached(options: Intl.NumberFormatOptions) {
  const byLocale = new Map<AppLocale, Intl.NumberFormat>();
  return (locale: AppLocale) => {
    let formatter = byLocale.get(locale);
    if (!formatter) {
      formatter = new Intl.NumberFormat(intlLocale(locale), options);
      byLocale.set(locale, formatter);
    }
    return formatter;
  };
}

const hours = cached({ maximumFractionDigits: 2 });
const percent = cached({ maximumFractionDigits: 1 });
const count = cached({});

/** "4,5 h" / "4.5 h": hours, up to two decimals (the API rounds to two). */
export function formatHours(value: number, locale: AppLocale): string {
  return `${hours(locale).format(value)} h`;
}

/**
 * "81,3 %" (pt-PT) or "81.3%" (English) for the fraction 0.8125 (the API sends it with four decimals). Portuguese writes the number, a
 * no-break space and the sign (`Intl`'s own pt-PT percent leaves the space out); English writes no space.
 */
export function formatOccupation(fraction: number, locale: AppLocale): string {
  const number = percent(locale).format(fraction * 100);
  return locale === "en" ? `${number}%` : `${number} %`;
}

/** "1 234" (pt-PT) or "1,234" (English) for four digits and more, "123" below. */
export function formatCount(value: number, locale: AppLocale): string {
  return count(locale).format(value);
}
