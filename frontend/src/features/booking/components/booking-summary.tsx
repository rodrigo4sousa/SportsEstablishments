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
  /** What the player can do next: confirm, sign in, or nothing. */
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
      <div className="flex flex-wrap items-center gap-x-6 gap-y-2">
        <Link href="/bookings" className="text-sm font-medium underline underline-offset-4">
          Ver as minhas reservas
        </Link>
        <button type="button" onClick={onDismiss} className="text-sm underline underline-offset-4">
          {dismissLabel}
        </button>
      </div>
    </section>
  );
}
