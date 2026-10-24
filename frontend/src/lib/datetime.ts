/**
 * Dates and times are always shown in Lisbon time, whatever the browser's time zone (T04).
 * The API sends instants as ISO 8601 strings in UTC.
 */
export const LISBON_TIME_ZONE = "Europe/Lisbon";

const LOCALE = "pt-PT";

const timeFormatter = new Intl.DateTimeFormat(LOCALE, {
  timeZone: LISBON_TIME_ZONE,
  hour: "2-digit",
  minute: "2-digit",
  hourCycle: "h23",
});

const dateFormatter = new Intl.DateTimeFormat(LOCALE, {
  timeZone: LISBON_TIME_ZONE,
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
});

const longDateFormatter = new Intl.DateTimeFormat(LOCALE, {
  timeZone: LISBON_TIME_ZONE,
  weekday: "long",
  day: "numeric",
  month: "long",
});

const dateKeyFormatter = new Intl.DateTimeFormat("en-CA", {
  timeZone: LISBON_TIME_ZONE,
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
});

// An ISO instant must carry its zone ("Z" or "+01:00"); without it the browser would read it as local time.
const ZONED_ISO_INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})$/;

export type Instant = Date | string;

function toDate(instant: Instant): Date {
  if (instant instanceof Date) {
    return instant;
  }
  if (!ZONED_ISO_INSTANT.test(instant)) {
    throw new Error(`Expected an ISO 8601 instant with a time zone, got "${instant}".`);
  }
  return new Date(instant);
}

/** "14:30" */
export function formatLisbonTime(instant: Instant): string {
  return timeFormatter.format(toDate(instant));
}

/** "01/08/2026" */
export function formatLisbonDate(instant: Instant): string {
  return dateFormatter.format(toDate(instant));
}

/** "sábado, 1 de agosto" */
export function formatLisbonLongDate(instant: Instant): string {
  return longDateFormatter.format(toDate(instant));
}

/** "01/08/2026 14:30" */
export function formatLisbonDateTime(instant: Instant): string {
  return `${formatLisbonDate(instant)} ${formatLisbonTime(instant)}`;
}

const ONE_HOUR_MS = 60 * 60 * 1000;

/**
 * When clocks go back (last Sunday of October) the Lisbon wall-clock hour 01:00–02:00 happens twice.
 * Returns "(verão)" for the first occurrence (WEST), "(inverno)" for the second (WET), and null otherwise.
 */
export function lisbonRepeatedHourLabel(instant: Instant): "(verão)" | "(inverno)" | null {
  const date = toDate(instant);
  const wallClock = formatLisbonDateTime(date);

  if (formatLisbonDateTime(new Date(date.getTime() + ONE_HOUR_MS)) === wallClock) {
    return "(verão)";
  }
  if (formatLisbonDateTime(new Date(date.getTime() - ONE_HOUR_MS)) === wallClock) {
    return "(inverno)";
  }
  return null;
}

/** "14:30", or "01:30 (verão)" / "01:30 (inverno)" inside the repeated hour. Use it for slot times. */
export function formatLisbonSlotTime(instant: Instant): string {
  const label = lisbonRepeatedHourLabel(instant);
  const time = formatLisbonTime(instant);
  return label ? `${time} ${label}` : time;
}

/** Lisbon calendar date as "YYYY-MM-DD", for API query parameters and date pickers. */
export function lisbonDateKey(instant: Instant): string {
  return dateKeyFormatter.format(toDate(instant));
}

/** Today's date in Lisbon as "YYYY-MM-DD"; near midnight it can differ from the browser's date. */
export function lisbonToday(now: Date = new Date()): string {
  return lisbonDateKey(now);
}

const DATE_KEY = /^(\d{4})-(\d{2})-(\d{2})$/;

/** True for a real calendar date written "YYYY-MM-DD" ("2026-02-30" is not). */
export function isDateKey(value: string): boolean {
  const match = DATE_KEY.exec(value);
  if (!match) {
    return false;
  }
  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const date = new Date(Date.UTC(year, month - 1, day));
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
}

/** The calendar date `days` after (or before, if negative) a "YYYY-MM-DD" date. Pure calendar arithmetic, no time zone involved. */
export function addDaysToDateKey(dateKey: string, days: number): string {
  if (!isDateKey(dateKey)) {
    throw new Error(`Expected a date as YYYY-MM-DD, got "${dateKey}".`);
  }
  const [year, month, day] = dateKey.split("-").map(Number);
  return new Date(Date.UTC(year, month - 1, day + days)).toISOString().slice(0, 10);
}
