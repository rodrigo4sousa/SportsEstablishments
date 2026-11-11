"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { useCallback } from "react";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import { useFormats, type Formats } from "@/i18n/formats";
import { useFocusOn } from "@/lib/focus";
import { OCCURRENCE_REASON_CODES, type Occurrence, type RecurringResult, type SkippedDate } from "../api";
import { MAX_WEEKS } from "../selection";

const WEEK_OPTIONS = Array.from({ length: MAX_WEEKS }, (_, index) => index + 1);

/** US20: "Repetir semanalmente" and for how many weeks, the first one included (1 = no repetition). */
export function WeeksSelect({ weeks, disabled, onChange }: { weeks: number; disabled?: boolean; onChange: (weeks: number) => void }) {
  const t = useTranslations("booking.recurring");
  return (
    <div className="flex flex-col gap-2 sm:max-w-xs">
      <Label htmlFor="booking-weeks">{t("repeat")}</Label>
      <NativeSelect id="booking-weeks" value={weeks} disabled={disabled} onChange={(event) => onChange(Number(event.target.value))}>
        {WEEK_OPTIONS.map((option) => (
          <option key={option} value={option}>
            {t("weeks", { count: option })}
          </option>
        ))}
      </NativeSelect>
    </div>
  );
}

const when = (formats: Formats, startsAt: string, endsAt: string) =>
  `${formats.longDate(startsAt)}, ${formats.slotTime(startsAt)} – ${formats.slotTime(endsAt)}`;

/** Why a date of a series is left out, in words, in the page's language. */
function useOccurrenceReason() {
  const t = useTranslations("booking.recurring.reasons");
  return useCallback(
    (code: string | null) => (code === null ? "" : (OCCURRENCE_REASON_CODES as readonly string[]).includes(code) ? t(code as (typeof OCCURRENCE_REASON_CODES)[number]) : t("other")),
    [t],
  );
}

/** US20: every date of the series with its price, free or unavailable and why. Never says who booked a date. */
export function RecurringPreviewList({ occurrences }: { occurrences: readonly Occurrence[] }) {
  const t = useTranslations("booking.recurring");
  const formats = useFormats();
  const occurrenceReason = useOccurrenceReason();
  const free = occurrences.filter((occurrence) => occurrence.status === "free").length;
  return (
    <div className="flex flex-col gap-2">
      <p className="text-sm text-muted-foreground">
        {free === occurrences.length
          ? t("allFree", { count: occurrences.length })
          : free === 0
            ? t("noneFree")
            : t("someFree", { free, count: occurrences.length })}
      </p>
      <ul aria-label={t("dates")} className="flex flex-col divide-y rounded-md border">
        {occurrences.map((occurrence) => (
          <li key={occurrence.startsAt} data-status={occurrence.status} className="flex flex-wrap items-baseline justify-between gap-x-4 px-3 py-2 text-sm">
            <span className={occurrence.status === "free" ? "tabular-nums" : "tabular-nums text-muted-foreground"}>{when(formats, occurrence.startsAt, occurrence.endsAt)}</span>{" "}
            <span className="tabular-nums">
              {formats.price(occurrence.price)} ·{" "}
              {occurrence.status === "free" ? (
                <span className="font-medium">{t("free")}</span>
              ) : (
                <span className="font-medium text-destructive">{t("unavailable", { reason: occurrenceReason(occurrence.reason) })}</span>
              )}
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function SkippedList({ skipped }: { skipped: readonly SkippedDate[] }) {
  const t = useTranslations("booking.recurring");
  const formats = useFormats();
  const occurrenceReason = useOccurrenceReason();
  return (
    <ul aria-label={t("skippedList")} className="flex flex-col gap-1 text-sm">
      {skipped.map((date) => (
        <li key={date.startsAt} className="tabular-nums">
          {when(formats, date.startsAt, date.endsAt)}: {occurrenceReason(date.reason)}
        </li>
      ))}
    </ul>
  );
}

/** US20: what the series made, and what was left out and why. */
export function RecurringConfirmation({ result, onDismiss }: { result: RecurringResult; onDismiss: () => void }) {
  // Same rule as the single confirmation: focus moves to the heading, and the section is not a live region (UX-002).
  const focus = useFocusOn<HTMLHeadingElement>();
  const t = useTranslations("booking.recurring");
  const summary = useTranslations("booking.summary");
  const booking = useTranslations("booking");
  const formats = useFormats();
  return (
    <section aria-labelledby="series-confirmed" className="flex flex-col gap-4 rounded-lg border border-emerald-600/40 bg-emerald-50 p-4 dark:bg-emerald-950/30">
      <h3 id="series-confirmed" {...focus} className="font-semibold">
        {t("confirmed", { count: result.created.length })}
      </h3>
      <ul aria-label={t("made")} className="flex flex-col gap-1 text-sm">
        {result.created.map((booking) => (
          <li key={booking.id} className="tabular-nums">
            {when(formats, booking.startsAt, booking.endsAt)} · {formats.price(booking.price)}
          </li>
        ))}
      </ul>
      {result.skipped.length > 0 ? (
        <div className="flex flex-col gap-2">
          <p className="text-sm font-medium">{t("leftOut")}</p>
          <SkippedList skipped={result.skipped} />
        </div>
      ) : null}
      <p className="text-sm text-muted-foreground">{t("note")}</p>
      <div className="flex flex-wrap items-center gap-x-6 gap-y-2">
        <Link href="/bookings" className="text-sm font-medium underline underline-offset-4">
          {summary("myBookings")}
        </Link>
        <button type="button" onClick={onDismiss} className="text-sm underline underline-offset-4">
          {booking("seeOtherTimes")}
        </button>
      </div>
    </section>
  );
}

/** US20: no date could be booked: the dates and why, below the error message. */
export function RecurringNoneBooked({ skipped }: { skipped: readonly SkippedDate[] }) {
  const t = useTranslations("booking.recurring");
  return skipped.length === 0 ? null : (
    <div className="flex flex-col gap-2 rounded-md border p-3">
      <p className="text-sm font-medium">{t("requested")}</p>
      <SkippedList skipped={skipped} />
    </div>
  );
}
