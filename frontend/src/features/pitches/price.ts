/**
 * US07 price rules, for immediate feedback only: the API validates again and has the last word (the maximum is
 * provisional, see US07). Prices are typed and shown the pt-PT way (comma decimals) but sent as numbers.
 */
export const PRICE_MAX = 500;
export const PRICE_DECIMALS = 2;

export const PRICE_MESSAGES = {
  required: "Indica o preço por slot.",
  invalid: "Indica um preço válido, por exemplo 12,50.",
  notPositive: "O preço por slot tem de ser maior que 0 €.",
  tooHigh: `O preço por slot não pode passar de ${PRICE_MAX} €.`,
  decimals: "O preço por slot pode ter no máximo duas casas decimais.",
} as const;

export type PriceResult = { ok: true; value: number } | { ok: false; message: string };

// Digits with an optional comma or dot decimal part; a "€" and spaces around are fine. No thousands separators:
// a price never reaches 1 000 €, and "1.234" would be ambiguous.
const PRICE_PATTERN = /^(\d+)(?:[.,](\d+))?$/;

/** Reads what the owner typed ("12,50", "12.50", "12 €") as a price in euros. */
export function parsePrice(text: string): PriceResult {
  const typed = text.replaceAll(/[\s ]/g, "").replace(/€$/, "");
  if (typed === "") {
    return { ok: false, message: PRICE_MESSAGES.required };
  }

  const match = PRICE_PATTERN.exec(typed);
  if (!match) {
    return { ok: false, message: PRICE_MESSAGES.invalid };
  }

  // Trailing zeros are not decimals that matter: "12,500" is 12.5, like the API reads 12.500.
  const decimals = (match[2] ?? "").replace(/0+$/, "");
  if (decimals.length > PRICE_DECIMALS) {
    return { ok: false, message: PRICE_MESSAGES.decimals };
  }

  const value = Number(`${match[1]}.${decimals || "0"}`);
  if (value <= 0) {
    return { ok: false, message: PRICE_MESSAGES.notPositive };
  }
  if (value > PRICE_MAX) {
    return { ok: false, message: PRICE_MESSAGES.tooHigh };
  }
  return { ok: true, value };
}

const euros = new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" });

/** "12,50 €": how a price is shown. */
export function formatPrice(value: number): string {
  return euros.format(value);
}

/** How a price is put in the edit field: comma decimals, no symbol ("12,50"). */
export function priceInputValue(value: number): string {
  return value.toFixed(PRICE_DECIMALS).replace(".", ",");
}
