"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { ResultNotice } from "@/components/result-notice";
import { Button } from "@/components/ui/button";
import { useSportLabel } from "@/features/pitches/sports";
import { useFormats } from "@/i18n/formats";
import { useFocusOn, useRestoreFocus } from "@/lib/focus";
import { useStatusLabel } from "../status";
import type { MyBooking } from "../types";

type MyBookingItemProps = {
  booking: MyBooking;
  /** Only upcoming bookings can be cancelled: a confirmed booking that has ended (listed under «Anteriores») shows no button. */
  cancellable: boolean;
  /** The cancellation of this booking is waiting for the player's confirmation. */
  confirming: boolean;
  /** This booking is being cancelled right now. */
  cancelling: boolean;
  /** The API's refusal of what the player just asked about this booking: shown here, where the player pressed (UX-003). */
  refusal: string | null;
  onAskToCancel: () => void;
  onKeep: () => void;
  onConfirmCancel: () => void;
};

/**
 * US19: one of the player's bookings, as a compact row (UX-022): when, the club (a link to its page), the pitch, the price and the
 * status. The day is the heading of the group the row is in, so it is not repeated. A confirmed booking has a cancel button:
 * enabled until the club's deadline (it asks to confirm first), and disabled after it, saying when the deadline was. Every time goes
 * through `lib/datetime.ts`.
 */
export function MyBookingItem({ booking, cancellable, confirming, cancelling, refusal, onAskToCancel, onKeep, onConfirmCancel }: MyBookingItemProps) {
  const t = useTranslations("myBookings.item");
  const formats = useFormats();
  const sportLabel = useSportLabel();
  const statusLabel = useStatusLabel();
  const isConfirmed = booking.status === "confirmed";
  const deadline = booking.cancellationDeadline;
  const explanationId = `cancel-info-${booking.id}`;
  // UX-002: asking replaces the «Cancelar reserva» button, so focus goes to the question; keeping the booking puts it back.
  const confirmationFocus = useFocusOn<HTMLDivElement>(confirming);
  const askButton = useRestoreFocus<HTMLButtonElement>(confirming);

  return (
    <li aria-label={`${booking.clubName}, ${booking.pitchName}`} className="flex flex-col gap-2 rounded-lg border p-3 sm:p-4">
      <div className="flex flex-wrap items-start justify-between gap-x-3 gap-y-1">
        <p className="font-medium">
          <span data-slot="booking-time" className="tabular-nums">
            {formats.slotTime(booking.startsAt)} – {formats.slotTime(booking.endsAt)}
          </span>
          {" · "}
          {/* The link is padded and pulled back, so it is easy to hit on a phone without making the row taller. */}
          <Link href={`/clubs/${encodeURIComponent(booking.clubSlug)}`} className="-my-2 inline-block py-2 underline-offset-4 hover:underline">
            {booking.clubName}
          </Link>
        </p>
        <p
          className={
            isConfirmed
              ? "rounded-full bg-emerald-100 px-2.5 py-0.5 text-xs font-medium text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300"
              : "rounded-full bg-muted px-2.5 py-0.5 text-xs font-medium text-muted-foreground"
          }
        >
          <span className="sr-only">{t("status")}</span>
          {statusLabel(booking.status)}
        </p>
      </div>

      <p data-slot="booking-details" className="text-sm text-muted-foreground">
        {booking.pitchName} · {sportLabel(booking.sport)} · <span className="tabular-nums">{formats.price(booking.price)}</span>
        {booking.series ? (
          <>
            {" · "}
            {t.rich("weekly", { position: booking.series.position, count: booking.series.count, num: (chunks) => <span className="tabular-nums">{chunks}</span> })}
          </>
        ) : null}
      </p>

      {refusal ? <ResultNotice kind="error">{refusal}</ResultNotice> : null}

      {isConfirmed && cancellable ? (
        confirming ? (
          <div role="group" aria-label={t("confirmGroup")} {...confirmationFocus} className="flex flex-col gap-3 rounded-md border border-destructive/40 bg-destructive/5 p-3">
            <p className="text-sm">{t("confirmQuestion")}</p>
            <div className="flex flex-wrap items-center gap-3">
              <Button type="button" size="cta" variant="destructive" disabled={cancelling} onClick={onConfirmCancel}>
                {cancelling ? t("cancelling") : t("confirmCancel")}
              </Button>
              <Button type="button" size="cta" variant="outline" disabled={cancelling} onClick={onKeep}>
                {t("keep")}
              </Button>
            </div>
          </div>
        ) : (
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <Button ref={askButton} type="button" size="cta" variant="outline" disabled={!booking.canCancel} aria-describedby={explanationId} onClick={onAskToCancel}>
              {t("cancel")}
            </Button>
            <p id={explanationId} className="text-sm text-muted-foreground">
              {deadline === null
                ? null
                : booking.canCancel
                  ? t("canCancelUntil", { deadline: formats.dateTime(deadline) })
                  : t("deadlinePassed", { deadline: formats.dateTime(deadline) })}
            </p>
          </div>
        )
      ) : null}
    </li>
  );
}
