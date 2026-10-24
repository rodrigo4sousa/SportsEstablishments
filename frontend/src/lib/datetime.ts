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

/**
 * "sábado, 1 de agosto" for a calendar date written "YYYY-MM-DD" (a date key, with no time of day). Noon UTC is the
 * same calendar day in Lisbon all year round, so the formatter cannot land on the day before or after.
 */
export function formatDateKeyLong(dateKey: string): string {
  if (!isDateKey(dateKey)) {
    throw new Error(`Expected a date as YYYY-MM-DD, got "${dateKey}".`);
  }
  return formatLisbonLongDate(`${dateKey}T12:00:00Z`);
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

/** Now in Lisbon as "YYYY-MM-DDTHH:mm" (the format of the owner's date-time fields), to compare with what was typed. */
export function lisbonNowLocal(now: Date = new Date()): string {
  return `${lisbonDateKey(now)}T${formatLisbonTime(now)}`;
}

// ---- The Lisbon day as a stretch of real elapsed time (the owner's calendar, US11)

const offsetFormatter = new Intl.DateTimeFormat("en-CA", {
  timeZone: LISBON_TIME_ZONE,
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
  hourCycle: "h23",
});

const MINUTE_MS = 60_000;
const LOCAL_TIME = /^([01]\d|2[0-3]):([0-5]\d)$/;

/** How many minutes Lisbon is ahead of UTC at an instant (0 in winter, 60 in summer). */
function lisbonOffsetMinutes(instantMs: number): number {
  const parts: Record<string, string> = {};
  for (const part of offsetFormatter.formatToParts(instantMs)) {
    parts[part.type] = part.value;
  }
  const wallClockAsUtc = Date.UTC(Number(parts.year), Number(parts.month) - 1, Number(parts.day), Number(parts.hour), Number(parts.minute));
  return Math.round((wallClockAsUtc - Math.floor(instantMs / MINUTE_MS) * MINUTE_MS) / MINUTE_MS);
}

/**
 * The instant of a Lisbon wall-clock time ("HH:mm", midnight by default) on a date ("YYYY-MM-DD"), by the same decided
 * rules as the API's `ClubTime`: a time that does not exist (clocks go forward) moves forward past the gap (01:30 →
 * 02:30 WEST), and a time that happens twice (clocks go back) is its first occurrence (WEST).
 */
export function lisbonInstant(dateKey: string, time = "00:00"): Date {
  const clock = LOCAL_TIME.exec(time);
  if (!isDateKey(dateKey) || !clock) {
    throw new Error(`Expected a date as YYYY-MM-DD and a time as HH:mm, got "${dateKey}" and "${time}".`);
  }
  const [year, month, day] = dateKey.split("-").map(Number);
  const wallClockAsUtc = Date.UTC(year, month - 1, day, Number(clock[1]), Number(clock[2]));
  // Lisbon is on UTC+1 or UTC+0: try both, keep the offsets that really apply at the instant they give, earliest first.
  const matching = [60, 0].map((offset) => ({ offset, ms: wallClockAsUtc - offset * MINUTE_MS })).filter(({ offset, ms }) => lisbonOffsetMinutes(ms) === offset);
  return new Date(matching.length > 0 ? Math.min(...matching.map(({ ms }) => ms)) : wallClockAsUtc);
}

/** The instant a Lisbon day starts: local midnight. */
export function lisbonDayStart(dateKey: string): Date {
  return lisbonInstant(dateKey);
}

/** How many minutes the Lisbon day lasts: 1440, but 1500 when the clocks go back (25 October 2026) and 1380 when they go forward (29 March 2026). */
export function lisbonDayMinutes(dateKey: string): number {
  return (lisbonDayStart(addDaysToDateKey(dateKey, 1)).getTime() - lisbonDayStart(dateKey).getTime()) / MINUTE_MS;
}

/** Real elapsed minutes from the start of a Lisbon day to an instant (negative before the day, past its length after it). */
export function minutesFromLisbonDayStart(instant: Instant, dateKey: string): number {
  return (toDate(instant).getTime() - lisbonDayStart(dateKey).getTime()) / MINUTE_MS;
}

/** Real elapsed minutes from the start of a Lisbon day to a wall-clock time of that day ("HH:mm"). */
export function lisbonLocalMinutes(dateKey: string, time: string): number {
  return minutesFromLisbonDayStart(lisbonInstant(dateKey, time), dateKey);
}

/** The instant that is `minutes` of real elapsed time after the start of a Lisbon day. */
export function lisbonInstantAtMinute(dateKey: string, minutes: number): Date {
  return new Date(lisbonDayStart(dateKey).getTime() + minutes * MINUTE_MS);
}
