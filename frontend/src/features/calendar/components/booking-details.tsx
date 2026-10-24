"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { statusLabel } from "@/features/my-bookings/status";
import { formatPrice } from "@/features/pitches/price";
import { sportLabel } from "@/features/pitches/sports";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { formatLisbonDate, formatLisbonDateTime, formatLisbonLongDate, formatLisbonSlotTime } from "@/lib/datetime";
import { calendarBookingQueryKey, fetchClubBooking } from "../api";
import { CancelBookingForm } from "./cancel-booking-form";

type BookingDetailsProps = {
  clubId: string;
  bookingId: string;
  onClose: () => void;
};

/**
 * US11 and US12: one booking of the club, opened from the calendar: where and when, the price, the status and the player's
 * name (the only thing the owner knows about the player). A confirmed booking that has not started can be cancelled; that
 * is the API's `canCancel`, the screen never works it out. A booking that is not there (any more) says so.
 */
export function BookingDetails({ clubId, bookingId, onClose }: BookingDetailsProps) {
  const [cancelling, setCancelling] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [refusal, setRefusal] = useState<string | null>(null);
  const booking = useQuery({
    queryKey: calendarBookingQueryKey(clubId, bookingId),
    queryFn: () => fetchClubBooking(clubId, bookingId),
    // A booking that is not there stays not there.
    retry: false,
    refetchOnWindowFocus: false,
  });

  const close = () => {
    setCancelling(false);
    setNotice(null);
    setRefusal(null);
    onClose();
  };

  return (
    <section aria-label="Detalhes da reserva" className="flex flex-col gap-4 rounded-lg border p-4">
      <div className="flex items-start justify-between gap-3">
        <h3 className="font-semibold">Reserva</h3>
        <Button type="button" variant="ghost" size="sm" onClick={close}>
          Fechar
        </Button>
      </div>

      {booking.isPending ? (
        <p role="status" className="text-muted-foreground">
          A carregar a reserva…
        </p>
      ) : booking.isError ? (
        <div role="alert" className="flex flex-col items-start gap-3">
          <p>
            {booking.error instanceof ApiError && booking.error.code === "booking_not_found"
              ? errorMessage("booking_not_found")
              : errorMessage(booking.error instanceof ApiError ? booking.error.code : "unknown_error")}
          </p>
          {booking.error instanceof ApiError && booking.error.status === 404 ? null : (
            <Button type="button" variant="outline" onClick={() => void booking.refetch()}>
              Tentar novamente
            </Button>
          )}
        </div>
      ) : (
        <>
          <dl className="grid gap-x-6 gap-y-1 text-sm sm:grid-cols-[max-content_1fr]">
            <dt className="text-muted-foreground">Jogador</dt>
            <dd className="font-medium">{booking.data.playerName}</dd>
            <dt className="text-muted-foreground">Campo</dt>
            <dd>
              {booking.data.pitchName} · {sportLabel(booking.data.sport)}
            </dd>
            <dt className="text-muted-foreground">Data</dt>
            <dd>
              {formatLisbonLongDate(booking.data.startsAt)} ({formatLisbonDate(booking.data.startsAt)})
            </dd>
            <dt className="text-muted-foreground">Hora</dt>
            <dd className="tabular-nums">
              {formatLisbonSlotTime(booking.data.startsAt)} – {formatLisbonSlotTime(booking.data.endsAt)}
            </dd>
            <dt className="text-muted-foreground">Preço</dt>
            <dd className="tabular-nums">{formatPrice(booking.data.price)}</dd>
            <dt className="text-muted-foreground">Estado</dt>
            <dd>{statusLabel(booking.data.status)}</dd>
            <dt className="text-muted-foreground">Reservada em</dt>
            <dd className="tabular-nums">{formatLisbonDateTime(booking.data.createdAt)}</dd>
            {booking.data.cancelledAt ? (
              <>
                <dt className="text-muted-foreground">Cancelada em</dt>
                <dd className="tabular-nums">{formatLisbonDateTime(booking.data.cancelledAt)}</dd>
              </>
            ) : null}
            {booking.data.cancellationReason ? (
              <>
                <dt className="text-muted-foreground">Motivo</dt>
                <dd>{booking.data.cancellationReason}</dd>
              </>
            ) : null}
          </dl>

          {refusal ? (
            <p role="alert" className="rounded-md border border-destructive/40 bg-destructive/10 px-3 py-2 text-sm text-destructive">
              {refusal}
            </p>
          ) : null}

          {notice ? (
            <p role="status" className="rounded-md border border-emerald-600/40 bg-emerald-50 px-3 py-2 text-sm dark:bg-emerald-950/30">
              {notice}
            </p>
          ) : null}

          {booking.data.canCancel ? (
            cancelling ? (
              <CancelBookingForm
                clubId={clubId}
                booking={booking.data}
                onKeep={() => setCancelling(false)}
                onRefused={(message) => {
                  setCancelling(false);
                  setRefusal(message);
                }}
                onCancelled={() => {
                  setCancelling(false);
                  setRefusal(null);
                  setNotice("Reserva cancelada. O horário voltou a ficar livre.");
                }}
              />
            ) : (
              <div>
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => {
                    setRefusal(null);
                    setCancelling(true);
                  }}
                >
                  Cancelar reserva
                </Button>
              </div>
            )
          ) : booking.data.status === "confirmed" ? (
            <p className="text-sm text-muted-foreground">Esta reserva já começou, por isso já não pode ser cancelada.</p>
          ) : null}
        </>
      )}
    </section>
  );
}
