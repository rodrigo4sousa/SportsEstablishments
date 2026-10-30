"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { FormField } from "@/components/form-field";
import { Button } from "@/components/ui/button";
import { errorMessage } from "@/lib/api/error-messages";
import { isExpectedApiError, unexpectedErrorMessage } from "@/lib/api/expected-error";
import { showApiFieldErrors } from "@/lib/api/form-errors";
import { calendarBookingQueryKey, calendarQueryKey, cancelClubBooking } from "../api";
import { CANCEL_REASON_MAX_LENGTH, cancelBookingSchema, type CancelBookingData, type CancelBookingValues } from "../schemas/cancel-booking";
import type { ClubBooking } from "../types";

/** The codes that mean the booking is not what the owner thought: say so, and load it again. */
const STALE_CODES = new Set(["booking_not_confirmed", "booking_already_started", "booking_not_found", "club_not_found"]);

type CancelBookingFormProps = {
  clubId: string;
  booking: ClubBooking;
  /** Called with the cancelled booking once the API has done it. */
  onCancelled: (booking: ClubBooking) => void;
  /** Called when the owner decides to keep the booking. */
  onKeep: () => void;
  /** Called with the message when the API says the booking is not what the owner thought (the form may disappear after the reload). */
  onRefused: (message: string) => void;
};

/**
 * US12: the owner cancels a booking of the club with a reason (required, up to 200 characters). There is no cancellation
 * window for the club; the API refuses a booking that has started or is not confirmed any more, and then the booking and
 * the calendar are loaded again so the screen says what is true now.
 */
export function CancelBookingForm({ clubId, booking, onCancelled, onKeep, onRefused }: CancelBookingFormProps) {
  const queryClient = useQueryClient();
  const form = useForm<CancelBookingValues, unknown, CancelBookingData>({ resolver: zodResolver(cancelBookingSchema), defaultValues: { reason: "" } });
  const { errors } = form.formState;

  // UX-002: the form replaces the button that opened it, so focus goes to its first field.
  const { setFocus } = form;
  useEffect(() => {
    setFocus("reason");
  }, [setFocus]);

  const cancel = useMutation({
    mutationFn: (reason: string) => cancelClubBooking(clubId, booking.id, reason),
    onSuccess: async (cancelled) => {
      queryClient.setQueryData(calendarBookingQueryKey(clubId, booking.id), cancelled);
      await queryClient.invalidateQueries({ queryKey: calendarQueryKey(clubId), refetchType: "all" });
      onCancelled(cancelled);
    },
    onError: async (error) => {
      if (!isExpectedApiError(error)) {
        toast.error(unexpectedErrorMessage(error));
        return;
      }
      if (error.code === "validation_failed" && showApiFieldErrors(form, error.fieldErrors, ["reason"])) {
        return;
      }
      if (STALE_CODES.has(error.code)) {
        onRefused(errorMessage(error.code));
        await queryClient.invalidateQueries({ queryKey: calendarQueryKey(clubId), refetchType: "all" });
        return;
      }
      toast.error(errorMessage(error.code));
    },
  });

  const reasonId = `cancel-reason-${booking.id}`;

  return (
    <form
      noValidate
      aria-label="Cancelar reserva"
      className="flex flex-col gap-4 rounded-md border border-destructive/40 bg-destructive/5 p-4"
      onSubmit={form.handleSubmit((values) => {
        cancel.mutate(values.reason);
      })}
    >
      <FormField
        id={reasonId}
        label="Motivo do cancelamento"
        hint={`Obrigatório, até ${CANCEL_REASON_MAX_LENGTH} caracteres.`}
        error={errors.reason?.message}
      >
        <textarea
          id={reasonId}
          rows={3}
          aria-invalid={errors.reason ? true : undefined}
          aria-describedby={errors.reason ? `${reasonId}-error` : `${reasonId}-hint`}
          className="min-h-20 w-full rounded-md border border-input bg-transparent px-3 py-2 text-base shadow-xs placeholder:text-muted-foreground focus-visible:border-ring aria-invalid:border-destructive aria-invalid:ring-destructive/20 md:text-sm dark:bg-input/30"
          {...form.register("reason")}
        />
      </FormField>

      <div className="flex flex-wrap items-center gap-3">
        <Button type="submit" variant="destructive" disabled={cancel.isPending}>
          {cancel.isPending ? "A cancelar…" : "Confirmar cancelamento"}
        </Button>
        <Button type="button" variant="outline" disabled={cancel.isPending} onClick={onKeep}>
          Manter reserva
        </Button>
      </div>
    </form>
  );
}
