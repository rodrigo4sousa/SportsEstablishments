"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useTranslations } from "next-intl";
import { ResultNotice } from "@/components/result-notice";
import { Button } from "@/components/ui/button";
import { useStatusLabel } from "@/features/my-bookings/status";
import { useSportLabel } from "@/features/pitches/sports";
import { useFormats } from "@/i18n/formats";
import { toast } from "sonner";
import { ApiError } from "@/lib/api/client";
import { UNKNOWN_ERROR, useApiErrors } from "@/lib/api/error-messages";
import { isExpectedApiError } from "@/lib/api/expected-error";
import { useFocusOn, useRestoreFocus } from "@/lib/focus";
import { calendarBookingQueryKey, calendarQueryKey, fetchClubBooking, markNoShow, undoNoShow } from "../api";
import { CancelBookingForm } from "./cancel-booking-form";

type BookingDetailsProps = {
  clubId: string;
  bookingId: string;
  onClose: () => void;
};

/** The codes that mean the booking is not what the owner thought: say so, and load it and the calendar again. */
const STALE_NO_SHOW_CODES = new Set(["booking_not_confirmed", "booking_not_ended", "booking_not_no_show", "booking_not_found", "club_not_found"]);

// Taking a no-show back can fail because the slot has a confirmed booking now; the general text for `slot_taken` is about booking, so
// `calendar.details.undoSlotTaken` says it for this action.

/**
 * US11 and US12: one booking of the club, opened from the calendar: where and when, the price, the status and the player's
 * name (the only thing the owner knows about the player). A confirmed booking that has not started can be cancelled; that
 * is the API's `canCancel`, the screen never works it out. A booking that is not there (any more) says so.
 */
export function BookingDetails({ clubId, bookingId, onClose }: BookingDetailsProps) {
  const t = useTranslations("calendar.details");
  const grid = useTranslations("calendar.grid");
  const common = useTranslations("common");
  const apiErrors = useApiErrors();
  const formats = useFormats();
  const sportLabel = useSportLabel();
  const statusLabel = useStatusLabel();
  const queryClient = useQueryClient();
  const [cancelling, setCancelling] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [refusal, setRefusal] = useState<string | null>(null);
  // UX-002, UX-004: opening a booking moves focus to its details, which also scrolls them into view, wherever the grid left them.
  const headingFocus = useFocusOn<HTMLHeadingElement>(bookingId);
  const cancelButton = useRestoreFocus<HTMLButtonElement>(cancelling);
  const booking = useQuery({
    queryKey: calendarBookingQueryKey(clubId, bookingId),
    queryFn: () => fetchClubBooking(clubId, bookingId),
    // A booking that is not there stays not there.
    retry: false,
    refetchOnWindowFocus: false,
  });

  // US15: «Marcar falta» and «Anular falta». The API says whether each one is possible (`canMarkNoShow`, `canUndoNoShow`); the
  // screen never works it out. After either, the booking and the calendar are loaded again.
  const noShow = useMutation({
    mutationFn: (action: "mark" | "undo") => (action === "mark" ? markNoShow(clubId, bookingId) : undoNoShow(clubId, bookingId)),
    onSuccess: async (changed, action) => {
      queryClient.setQueryData(calendarBookingQueryKey(clubId, bookingId), changed);
      setRefusal(null);
      setNotice(action === "mark" ? t("marked") : t("undone"));
      await queryClient.invalidateQueries({ queryKey: calendarQueryKey(clubId), refetchType: "all" });
    },
    onError: async (error, action) => {
      if (!isExpectedApiError(error)) {
        toast.error(apiErrors.unexpected(error));
        return;
      }
      setNotice(null);
      if (action === "undo" && error.code === "slot_taken") {
        setRefusal(t("undoSlotTaken"));
      } else if (STALE_NO_SHOW_CODES.has(error.code)) {
        setRefusal(apiErrors.message(error.code));
      } else {
        toast.error(apiErrors.message(error.code));
        return;
      }

      await queryClient.invalidateQueries({ queryKey: calendarQueryKey(clubId), refetchType: "all" });
    },
  });

  const close = () => {
    setCancelling(false);
    setNotice(null);
    setRefusal(null);
    onClose();
  };

  return (
    <section
      aria-label={t("label")}
      className="flex flex-col gap-4 rounded-lg border bg-background p-4"
      onKeyDown={(event) => {
        // Escape closes the details from anywhere inside them (UX-004); the calendar gives focus back to the booking.
        if (event.key === "Escape") {
          event.stopPropagation();
          close();
        }
      }}
    >
      <div className="flex items-start justify-between gap-3">
        <h3 {...headingFocus} className="font-semibold">
          {t("title")}
        </h3>
        <Button type="button" variant="ghost" size="sm" onClick={close}>
          {t("close")}
        </Button>
      </div>

      {booking.isPending ? (
        <p role="status" className="text-muted-foreground">
          {t("loading")}
        </p>
      ) : booking.isError ? (
        <div role="alert" className="flex flex-col items-start gap-3">
          <p>
            {booking.error instanceof ApiError && booking.error.code === "booking_not_found"
              ? apiErrors.message("booking_not_found")
              : apiErrors.message(booking.error instanceof ApiError ? booking.error.code : UNKNOWN_ERROR)}
          </p>
          {booking.error instanceof ApiError && booking.error.status === 404 ? null : (
            <Button type="button" variant="outline" onClick={() => void booking.refetch()}>
              {common("retry")}
            </Button>
          )}
        </div>
      ) : (
        <>
          <dl className="grid gap-x-6 gap-y-1 text-sm sm:grid-cols-[max-content_1fr]">
            <dt className="text-muted-foreground">{booking.data.createdByClub ? t("customer") : t("player")}</dt>
            <dd className="font-medium">{booking.data.bookerName}</dd>
            {booking.data.playerNoShows !== null ? (
              <>
                <dt className="text-muted-foreground">{t("noShowsLabel")}</dt>
                <dd>{t("noShows", { count: booking.data.playerNoShows })}</dd>
              </>
            ) : null}
            {booking.data.createdByClub ? (
              <>
                <dt className="text-muted-foreground">{t("phone")}</dt>
                <dd className="tabular-nums">{booking.data.customerPhone}</dd>
                <dt className="text-muted-foreground">{t("origin")}</dt>
                <dd>{grid("createdByClub")}</dd>
              </>
            ) : null}
            <dt className="text-muted-foreground">{t("pitch")}</dt>
            <dd>
              {booking.data.pitchName} · {sportLabel(booking.data.sport)}
            </dd>
            <dt className="text-muted-foreground">{t("date")}</dt>
            <dd>
              {formats.longDate(booking.data.startsAt)} ({formats.date(booking.data.startsAt)})
            </dd>
            <dt className="text-muted-foreground">{t("time")}</dt>
            <dd className="tabular-nums">
              {formats.slotTime(booking.data.startsAt)} – {formats.slotTime(booking.data.endsAt)}
            </dd>
            <dt className="text-muted-foreground">{t("price")}</dt>
            <dd className="tabular-nums">{formats.price(booking.data.price)}</dd>
            <dt className="text-muted-foreground">{t("status")}</dt>
            <dd>{statusLabel(booking.data.status)}</dd>
            <dt className="text-muted-foreground">{t("bookedAt")}</dt>
            <dd className="tabular-nums">{formats.dateTime(booking.data.createdAt)}</dd>
            {booking.data.cancelledAt ? (
              <>
                <dt className="text-muted-foreground">{t("cancelledAt")}</dt>
                <dd className="tabular-nums">{formats.dateTime(booking.data.cancelledAt)}</dd>
              </>
            ) : null}
            {booking.data.cancellationReason ? (
              <>
                <dt className="text-muted-foreground">{t("reason")}</dt>
                <dd>{booking.data.cancellationReason}</dd>
              </>
            ) : null}
          </dl>

          {/* The result of what the owner just did takes focus, so it is seen and read where the buttons were (UX-002). */}
          {refusal ? <ResultNotice kind="error">{refusal}</ResultNotice> : null}

          {notice ? <ResultNotice kind="success">{notice}</ResultNotice> : null}

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
                  setNotice(t("cancelled"));
                }}
              />
            ) : (
              <div>
                <Button
                  ref={cancelButton}
                  type="button"
                  variant="outline"
                  onClick={() => {
                    setRefusal(null);
                    setCancelling(true);
                  }}
                >
                  {t("cancel")}
                </Button>
              </div>
            )
          ) : booking.data.status === "confirmed" && !booking.data.canMarkNoShow ? (
            <p className="text-sm text-muted-foreground">{t("started")}</p>
          ) : null}

          {booking.data.canMarkNoShow ? (
            <div className="flex flex-col items-start gap-2">
              <p className="text-sm text-muted-foreground">{t("ended")}</p>
              <Button type="button" variant="outline" disabled={noShow.isPending} onClick={() => noShow.mutate("mark")}>
                {noShow.isPending ? t("marking") : t("markNoShow")}
              </Button>
            </div>
          ) : null}

          {booking.data.canUndoNoShow ? (
            <div>
              <Button type="button" variant="outline" disabled={noShow.isPending} onClick={() => noShow.mutate("undo")}>
                {noShow.isPending ? t("undoing") : t("undoNoShow")}
              </Button>
            </div>
          ) : null}
        </>
      )}
    </section>
  );
}
