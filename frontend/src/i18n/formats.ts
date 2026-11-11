import { useMemo } from "react";
import { useLocale, useTranslations } from "next-intl";
import { formatCount, formatHours, formatOccupation } from "@/features/statistics/format";
import { formatPrice } from "@/features/pitches/price";
import {
  formatDateKey,
  formatDateKeyLong,
  formatDateKeyMonth,
  formatDateKeyShort,
  formatLisbonDate,
  formatLisbonDateTime,
  formatLisbonDayHeading,
  formatLisbonLongDate,
  formatLisbonSlotTime,
  formatLisbonTime,
  type Instant,
} from "@/lib/datetime";
import type { AppLocale } from "./locales";

/** The words the formats need from the catalog (`time.*`), whatever gives them: `useTranslations` or `getTranslations`. */
type TimeWords = (key: "summerTime" | "winterTime" | "today" | "tomorrow" | "yesterday") => string;

/**
 * Every way the site shows a date, a time, a price or a number, in one language (T09): Lisbon time always (T04), `Intl` in pt-PT or
 * en-GB, and the catalog's words for "(verão)"/"(summer time)" and "Hoje"/"Today". Components get it from {@link useFormats}.
 */
export function createFormats(locale: AppLocale, words: TimeWords) {
  const repeatedHour = { summer: words("summerTime"), winter: words("winterTime") };
  const relativeDays = { today: words("today"), tomorrow: words("tomorrow"), yesterday: words("yesterday") };
  return {
    locale,
    /** "14:30" */
    time: (instant: Instant) => formatLisbonTime(instant),
    /** "14:30", or "01:30 (verão)" / "01:30 (summer time)" in the repeated October hour. */
    slotTime: (instant: Instant) => formatLisbonSlotTime(instant, repeatedHour),
    /** "01/08/2026" */
    date: (instant: Instant) => formatLisbonDate(instant),
    /** "01/08/2026 14:30" */
    dateTime: (instant: Instant) => formatLisbonDateTime(instant),
    /** "sábado, 1 de agosto" / "Saturday 1 August" */
    longDate: (instant: Instant) => formatLisbonLongDate(instant, locale),
    /** "Hoje · sábado, 1 de agosto" / "Today · Saturday 1 August" */
    dayHeading: (instant: Instant, now?: Date) => formatLisbonDayHeading(instant, locale, relativeDays, now),
    /** Date keys ("YYYY-MM-DD"): "01/08/2026" */
    dateKey: (dateKey: string) => formatDateKey(dateKey),
    /** "01/08" */
    dateKeyShort: (dateKey: string) => formatDateKeyShort(dateKey),
    /** "sábado, 1 de agosto" / "Saturday 1 August" */
    dateKeyLong: (dateKey: string) => formatDateKeyLong(dateKey, locale),
    /** "agosto de 2026" / "August 2026" */
    dateKeyMonth: (dateKey: string) => formatDateKeyMonth(dateKey, locale),
    /** "12,50 €" / "€12.50" */
    price: (value: number) => formatPrice(value, locale),
    /** "4,5 h" / "4.5 h" */
    hours: (value: number) => formatHours(value, locale),
    /** "81,3 %" / "81.3%" */
    occupation: (fraction: number) => formatOccupation(fraction, locale),
    /** "1 234" / "1,234" */
    count: (value: number) => formatCount(value, locale),
  };
}

export type Formats = ReturnType<typeof createFormats>;

/** The formats of the page's language, for a Client Component or a non-async Server Component. */
export function useFormats(): Formats {
  const locale = useLocale();
  const t = useTranslations("time");
  return useMemo(() => createFormats(locale, (key) => t(key)), [locale, t]);
}
