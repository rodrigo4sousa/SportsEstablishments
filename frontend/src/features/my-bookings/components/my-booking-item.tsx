"use client";

import Link from "next/link";
import { Button } from "@/components/ui/button";
import { formatPrice } from "@/features/pitches/price";
import { sportLabel } from "@/features/pitches/sports";
import { formatLisbonDate, formatLisbonDateTime, formatLisbonLongDate, formatLisbonSlotTime } from "@/lib/datetime";
import { useFocusOn, useRestoreFocus } from "@/lib/focus";
import { statusLabel } from "../status";
import type { MyBooking } from "../types";

type MyBookingItemProps = {
  booking: MyBooking;
  /** Only upcoming bookings can be cancelled: a confirmed booking that has ended (listed under «Anteriores») shows no button. */
  cancellable: boolean;
  /** The cancellation of this booking is waiting for the player's confirmation. */
  confirming: boolean;
  /** This booking is being cancelled right now. */
  cancelling: boolean;
  onAskToCancel: () => void;
  onKeep: () => void;
  onConfirmCancel: () => void;
};

/**
 * US19: one of the player's bookings: club (a link to its page), pitch, date and time in Lisbon, the price paid and the
 * status. A confirmed booking has a cancel button: enabled until the club's deadline (it asks to confirm first), and
 * disabled after it, saying when the deadline was. Every date goes through `lib/datetime.ts`.
 */
export function MyBookingItem({ booking, cancellable, confirming, cancelling, onAskToCancel, onKeep, onConfirmCancel }: MyBookingItemProps) {
  const isConfirmed = booking.status === "confirmed";
  const deadline = booking.cancellationDeadline;
  const explanationId = `cancel-info-${booking.id}`;
  // UX-002: asking replaces the «Cancelar reserva» button, so focus goes to the question; keeping the booking puts it back.
  const confirmationFocus = useFocusOn<HTMLDivElement>(confirming);
  const askButton = useRestoreFocus<HTMLButtonElement>(confirming);

  return (
    <li aria-label={`${booking.clubName}, ${booking.pitchName}`} className="flex flex-col gap-3 rounded-lg border p-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="flex flex-col gap-1">
          <h3 className="font-medium">
            <Link href={`/clubs/${encodeURIComponent(booking.clubSlug)}`} className="underline-offset-4 hover:underline">
              {booking.clubName}
            </Link>
          </h3>
          <p className="text-sm text-muted-foreground">
            {booking.pitchName} · {sportLabel(booking.sport)}
          </p>
        </div>
        <p
          className={
            isConfirmed
              ? "rounded-full bg-emerald-100 px-2.5 py-0.5 text-xs font-medium text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300"
              : "rounded-full bg-muted px-2.5 py-0.5 text-xs font-medium text-muted-foreground"
          }
        >
          <span className="sr-only">Estado: </span>
          {statusLabel(booking.status)}
        </p>
      </div>

      <dl className="grid gap-x-6 gap-y-1 text-sm sm:grid-cols-[max-content_1fr]">
        <dt className="text-muted-foreground">Data</dt>
        <dd>
          {formatLisbonLongDate(booking.startsAt)} ({formatLisbonDate(booking.startsAt)})
        </dd>
        <dt className="text-muted-foreground">Hora</dt>
        <dd className="tabular-nums">
          {formatLisbonSlotTime(booking.startsAt)} – {formatLisbonSlotTime(booking.endsAt)}
        </dd>
        <dt className="text-muted-foreground">Preço</dt>
        <dd className="tabular-nums">{formatPrice(booking.price)}</dd>
        {booking.series ? (
          <>
            <dt className="text-muted-foreground">Reserva semanal</dt>
            <dd className="tabular-nums">{booking.series.position} de {booking.series.count}</dd>
          </>
        ) : null}
      </dl>

      {isConfirmed && cancellable ? (
        confirming ? (
          <div role="group" aria-label="Confirmar cancelamento" {...confirmationFocus} className="flex flex-col gap-3 rounded-md border border-destructive/40 bg-destructive/5 p-3">
            <p className="text-sm">Queres mesmo cancelar esta reserva? O horário volta a ficar livre para outros jogadores.</p>
            <div className="flex flex-wrap items-center gap-3">
              <Button type="button" variant="destructive" disabled={cancelling} onClick={onConfirmCancel}>
                {cancelling ? "A cancelar…" : "Sim, cancelar reserva"}
              </Button>
              <Button type="button" variant="outline" disabled={cancelling} onClick={onKeep}>
                Manter reserva
              </Button>
            </div>
          </div>
        ) : (
          <div className="flex flex-wrap items-center gap-3">
            <Button ref={askButton} type="button" variant="outline" disabled={!booking.canCancel} aria-describedby={explanationId} onClick={onAskToCancel}>
              Cancelar reserva
            </Button>
            <p id={explanationId} className="text-sm text-muted-foreground">
              {deadline === null
                ? null
                : booking.canCancel
                  ? `Podes cancelar até ${formatLisbonDateTime(deadline)}.`
                  : `Já não é possível cancelar: o prazo terminou a ${formatLisbonDateTime(deadline)}.`}
            </p>
          </div>
        )
      ) : null}
    </li>
  );
}
