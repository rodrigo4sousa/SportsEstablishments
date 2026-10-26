const hours = new Intl.NumberFormat("pt-PT", { maximumFractionDigits: 2 });
const percent = new Intl.NumberFormat("pt-PT", { maximumFractionDigits: 1 });
const count = new Intl.NumberFormat("pt-PT");

/** "4,5 h": hours the pt-PT way, up to two decimals (the API rounds to two). */
export function formatHours(value: number): string {
  return `${hours.format(value)} h`;
}

/**
 * "81,3 %" for the fraction 0.8125 (the API sends it with four decimals): the number, a no-break space and the sign, as
 * Portuguese writes it (`Intl`'s own pt-PT percent leaves the space out).
 */
export function formatOccupation(fraction: number): string {
  return `${percent.format(fraction * 100)} %`;
}

/** "1 234" for four digits and more, "123" below: the pt-PT grouping. */
export function formatCount(value: number): string {
  return count.format(value);
}
