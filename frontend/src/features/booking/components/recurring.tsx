"use client";

import Link from "next/link";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import { formatPrice } from "@/features/pitches/price";
import { formatLisbonLongDate, formatLisbonSlotTime } from "@/lib/datetime";
import { useFocusOn } from "@/lib/focus";
import { occurrenceReason, type Occurrence, type RecurringResult, type SkippedDate } from "../api";
import { MAX_WEEKS } from "../selection";

const WEEK_OPTIONS = Array.from({ length: MAX_WEEKS }, (_, index) => index + 1);

/** US20: "Repetir semanalmente" and for how many weeks, the first one included (1 = no repetition). */
export function WeeksSelect({ weeks, disabled, onChange }: { weeks: number; disabled?: boolean; onChange: (weeks: number) => void }) {
  return (
    <div className="flex flex-col gap-2 sm:max-w-xs">
      <Label htmlFor="booking-weeks">Repetir semanalmente</Label>
      <NativeSelect id="booking-weeks" value={weeks} disabled={disabled} onChange={(event) => onChange(Number(event.target.value))}>
        {WEEK_OPTIONS.map((option) => (
          <option key={option} value={option}>
            {option === 1 ? "1 semana (sem repetição)" : `${option} semanas`}
          </option>
        ))}
      </NativeSelect>
    </div>
  );
}

const when = (startsAt: string, endsAt: string) => `${formatLisbonLongDate(startsAt)}, ${formatLisbonSlotTime(startsAt)} – ${formatLisbonSlotTime(endsAt)}`;

/** US20: every date of the series with its price, free or unavailable and why. Never says who booked a date. */
export function RecurringPreviewList({ occurrences }: { occurrences: readonly Occurrence[] }) {
  const free = occurrences.filter((occurrence) => occurrence.status === "free").length;
  return (
    <div className="flex flex-col gap-2">
      <p className="text-sm text-muted-foreground">
        {free === occurrences.length
          ? `Todas as ${occurrences.length} datas estão livres.`
          : free === 0
            ? "Nenhuma das datas está livre."
            : `${free} de ${occurrences.length} datas estão livres; as outras ficam de fora.`}
      </p>
      <ul aria-label="Datas da reserva semanal" className="flex flex-col divide-y rounded-md border">
        {occurrences.map((occurrence) => (
          <li key={occurrence.startsAt} data-status={occurrence.status} className="flex flex-wrap items-baseline justify-between gap-x-4 px-3 py-2 text-sm">
            <span className={occurrence.status === "free" ? "tabular-nums" : "tabular-nums text-muted-foreground"}>{when(occurrence.startsAt, occurrence.endsAt)}</span>{" "}
            <span className="tabular-nums">
              {formatPrice(occurrence.price)} ·{" "}
              {occurrence.status === "free" ? (
                <span className="font-medium">Livre</span>
              ) : (
                <span className="font-medium text-destructive">Indisponível: {occurrenceReason(occurrence.reason)}</span>
              )}
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function SkippedList({ skipped }: { skipped: readonly SkippedDate[] }) {
  return (
    <ul aria-label="Datas que ficaram de fora" className="flex flex-col gap-1 text-sm">
      {skipped.map((date) => (
        <li key={date.startsAt} className="tabular-nums">
          {when(date.startsAt, date.endsAt)}: {occurrenceReason(date.reason)}
        </li>
      ))}
    </ul>
  );
}

/** US20: what the series made, and what was left out and why. */
export function RecurringConfirmation({ result, onDismiss }: { result: RecurringResult; onDismiss: () => void }) {
  // Same rule as the single confirmation: focus moves to the heading, and the section is not a live region (UX-002).
  const focus = useFocusOn<HTMLHeadingElement>();
  return (
    <section aria-labelledby="series-confirmed" className="flex flex-col gap-4 rounded-lg border border-emerald-600/40 bg-emerald-50 p-4 dark:bg-emerald-950/30">
      <h3 id="series-confirmed" {...focus} className="font-semibold">
        {result.created.length === 1 ? "1 reserva confirmada" : `${result.created.length} reservas confirmadas`}
      </h3>
      <ul aria-label="Reservas feitas" className="flex flex-col gap-1 text-sm">
        {result.created.map((booking) => (
          <li key={booking.id} className="tabular-nums">
            {when(booking.startsAt, booking.endsAt)} · {formatPrice(booking.price)}
          </li>
        ))}
      </ul>
      {result.skipped.length > 0 ? (
        <div className="flex flex-col gap-2">
          <p className="text-sm font-medium">Ficaram de fora:</p>
          <SkippedList skipped={result.skipped} />
        </div>
      ) : null}
      <p className="text-sm text-muted-foreground">Cada reserva é independente: podes cancelar qualquer uma em «As minhas reservas». Enviámos-te um email com o resumo.</p>
      <div className="flex flex-wrap items-center gap-x-6 gap-y-2">
        <Link href="/bookings" className="text-sm font-medium underline underline-offset-4">
          Ver as minhas reservas
        </Link>
        <button type="button" onClick={onDismiss} className="text-sm underline underline-offset-4">
          Ver outros horários
        </button>
      </div>
    </section>
  );
}

/** US20: no date could be booked: the dates and why, below the error message. */
export function RecurringNoneBooked({ skipped }: { skipped: readonly SkippedDate[] }) {
  return skipped.length === 0 ? null : (
    <div className="flex flex-col gap-2 rounded-md border p-3">
      <p className="text-sm font-medium">Datas pedidas:</p>
      <SkippedList skipped={skipped} />
    </div>
  );
}
