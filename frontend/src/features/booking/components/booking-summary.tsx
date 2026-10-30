"use client";

import Link from "next/link";
import type { ReactNode } from "react";
import { formatPrice } from "@/features/pitches/price";
import { formatLisbonLongDate, formatLisbonSlotTime } from "@/lib/datetime";
import { useFocusOn } from "@/lib/focus";

export type BookingDetails = {
  clubName: string;
  pitchName: string;
  startsAt: string;
  endsAt: string;
  price: number;
};

/** The lines that say what is being booked, or was booked: club, pitch, date, time and price (US18). */
export function BookingDetailList({ details }: { details: BookingDetails }) {
  return (
    <dl className="grid gap-x-6 gap-y-2 sm:grid-cols-[max-content_1fr]">
      <dt className="text-muted-foreground">Clube</dt>
      <dd>{details.clubName}</dd>
      <dt className="text-muted-foreground">Campo</dt>
      <dd>{details.pitchName}</dd>
      <dt className="text-muted-foreground">Data</dt>
      <dd>{formatLisbonLongDate(details.startsAt)}</dd>
      <dt className="text-muted-foreground">Hora</dt>
      <dd className="tabular-nums">
        {formatLisbonSlotTime(details.startsAt)} – {formatLisbonSlotTime(details.endsAt)}
      </dd>
      <dt className="text-muted-foreground">Preço</dt>
      <dd className="tabular-nums">{formatPrice(details.price)}</dd>
    </dl>
  );
}

type BookingSummaryProps = {
  details: BookingDetails;
  /** What can still be chosen about this booking (the weekly repetition), and what it would book. */
  children: ReactNode;
};

/** US18: what the player is about to book, before confirming. */
export function BookingSummary({ details, children }: BookingSummaryProps) {
  return (
    <section aria-labelledby="booking-summary" className="flex flex-col gap-4 rounded-lg border p-4">
      <h3 id="booking-summary" className="font-semibold">
        Resumo da reserva
      </h3>
      <BookingDetailList details={details} />
      <div className="flex flex-wrap items-center gap-3">{children}</div>
    </section>
  );
}

type BookingActionsProps = {
  details: BookingDetails;
  /** What the player can do next: confirm, choose another slot, sign in. */
  children: ReactNode;
};

/**
 * What to do with the chosen slot. On a phone it is a bar that sticks to the bottom of the screen for as long as the booking
 * section is in view (UX-010): after choosing a slot in a long list, the main button is already on screen, with what it books
 * on top of it. From `sm` up it is simply the row of buttons under the summary. It is a sibling of the summary card, not inside
 * it: a sticky element cannot leave the box it is in.
 */
export function BookingActions({ details, children }: BookingActionsProps) {
  return (
    <div
      data-slot="booking-actions"
      className="sticky bottom-0 z-30 -mx-4 flex flex-col gap-2 border-t bg-background px-4 py-3 sm:static sm:z-auto sm:mx-0 sm:flex-row sm:flex-wrap sm:items-center sm:gap-3 sm:border-0 sm:bg-transparent sm:p-0"
    >
      {/* The summary above already says it all; this repeats it for the eye only, where the button is. */}
      <p aria-hidden="true" className="text-sm sm:hidden">
        <span className="font-medium tabular-nums">
          {formatLisbonSlotTime(details.startsAt)} – {formatLisbonSlotTime(details.endsAt)}
        </span>{" "}
        · <span className="tabular-nums">{formatPrice(details.price)}</span>
      </p>
      <div className="flex flex-wrap items-center gap-3">{children}</div>
    </div>
  );
}

type BookingConfirmationProps = {
  details: BookingDetails;
  onDismiss: () => void;
  dismissLabel: string;
};

/** US18: shown after the booking is made. */
export function BookingConfirmation({ details, onDismiss, dismissLabel }: BookingConfirmationProps) {
  // The summary this replaces had focus (the confirm button): focus goes to the heading, which a screen reader reads. The
  // section is not a live region, or it would be read twice (UX-002).
  const focus = useFocusOn<HTMLHeadingElement>();
  return (
    <section aria-labelledby="booking-confirmed" className="flex flex-col gap-4 rounded-lg border border-emerald-600/40 bg-emerald-50 p-4 dark:bg-emerald-950/30">
      <h3 id="booking-confirmed" {...focus} className="font-semibold">
        Reserva confirmada
      </h3>
      <BookingDetailList details={details} />
      <p className="text-sm text-muted-foreground">Enviámos-te um email de confirmação. Podes cancelar a reserva em «As minhas reservas», até ao prazo do clube.</p>
      <div className="flex flex-wrap items-center gap-x-6 gap-y-2">
        <Link href="/bookings" className="inline-flex min-h-11 items-center text-sm font-medium underline underline-offset-4 sm:min-h-0">
          Ver as minhas reservas
        </Link>
        <button type="button" onClick={onDismiss} className="min-h-11 text-sm underline underline-offset-4 sm:min-h-0">
          {dismissLabel}
        </button>
      </div>
    </section>
  );
}
