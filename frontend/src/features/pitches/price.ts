import { intlLocale, type AppLocale } from "@/i18n/locales";

/**
 * US07 price rules, for immediate feedback only: the API validates again and has the last word (the maximum is
 * provisional, see US07). Prices are typed with a comma or a dot ("12,50", "12.50") but sent as numbers.
 */
export const PRICE_MAX = 500;
export const PRICE_DECIMALS = 2;

/** Why a typed price was refused; the words are in the catalogs (`price.errors.*`), with `{max}` for `tooHigh`. */
export type PriceError = "required" | "invalid" | "notPositive" | "tooHigh" | "decimals";

export type PriceResult = { ok: true; value: number } | { ok: false; error: PriceError };

// Digits with an optional comma or dot decimal part; a "€" and spaces around are fine. No thousands separators:
// a price never reaches 1 000 €, and "1.234" would be ambiguous.
const PRICE_PATTERN = /^(\d+)(?:[.,](\d+))?$/;

/** Reads what the owner typed ("12,50", "12.50", "12 €", "€12.50") as a price in euros. */
export function parsePrice(text: string): PriceResult {
  const typed = text.replaceAll(/[\s ]/g, "").replace(/€$/, "").replace(/^€/, "");
  if (typed === "") {
    return { ok: false, error: "required" };
  }

  const match = PRICE_PATTERN.exec(typed);
  if (!match) {
    return { ok: false, error: "invalid" };
  }

  // Trailing zeros are not decimals that matter: "12,500" is 12.5, like the API reads 12.500.
  const decimals = (match[2] ?? "").replace(/0+$/, "");
  if (decimals.length > PRICE_DECIMALS) {
    return { ok: false, error: "decimals" };
  }

  const value = Number(`${match[1]}.${decimals || "0"}`);
  if (value <= 0) {
    return { ok: false, error: "notPositive" };
  }
  if (value > PRICE_MAX) {
    return { ok: false, error: "tooHigh" };
  }
  return { ok: true, value };
}

const euros = new Map<AppLocale, Intl.NumberFormat>();

/** "12,50 €" in pt-PT, "€12.50" in English: how a price is shown. */
export function formatPrice(value: number, locale: AppLocale): string {
  let formatter = euros.get(locale);
  if (!formatter) {
    formatter = new Intl.NumberFormat(intlLocale(locale), { style: "currency", currency: "EUR" });
    euros.set(locale, formatter);
  }
  return formatter.format(value);
}

/** How a price is put in the edit field: no symbol, the language's decimal mark ("12,50" or "12.50"). Both are read back. */
export function priceInputValue(value: number, locale: AppLocale): string {
  const text = value.toFixed(PRICE_DECIMALS);
  return locale === "en" ? text : text.replace(".", ",");
}
