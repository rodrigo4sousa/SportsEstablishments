"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { useForm, useWatch } from "react-hook-form";
import { toast } from "sonner";
import { FormField } from "@/components/form-field";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { NativeSelect } from "@/components/ui/native-select";
import { fetchSlots, slotsQueryKey, type PreviewSlot } from "@/features/pitches/api";
import { formatPrice } from "@/features/pitches/price";
import { DATE_OUT_OF_WINDOW, previewDateRange } from "@/features/pitches/schedule";
import { sportLabel } from "@/features/pitches/sports";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { isExpectedApiError, unexpectedErrorMessage } from "@/lib/api/expected-error";
import { showApiFieldErrors } from "@/lib/api/form-errors";
import { formatLisbonSlotTime, isDateKey } from "@/lib/datetime";
import { calendarQueryKey, createClubBooking } from "../api";
import { CUSTOMER_NAME_MAX_LENGTH, newBookingSchema, type NewBookingData, type NewBookingValues } from "../schemas/new-booking";
import type { CalendarPitch, ClubBooking } from "../types";

/** The codes that say the slot or the pitch is not what the owner saw: say so, and load the slots again. */
const REFRESH_CODES = new Set(["slot_taken", "slot_blocked", "slot_started", "invalid_slot", "outside_booking_window", "pitch_inactive", "pitch_not_found"]);

const FIELDS = ["pitchId", "startsAt", "customerName", "customerPhone"] as const satisfies ReadonlyArray<keyof NewBookingValues>;

export type NewBookingPrefill = {
  pitchId?: string;
  /** A Lisbon date ("YYYY-MM-DD"); outside today..+30 it is not used. */
  date?: string;
  /** An instant inside the slot to pre-select, if that slot is free (the time the owner clicked in the calendar). */
  startsAt?: string;
};

type NewBookingFormProps = {
  clubId: string;
  /** The pitches that can be booked: the active ones. */
  pitches: CalendarPitch[];
  prefill: NewBookingPrefill;
  onClose: () => void;
  /** Called with the new booking once the API has made it. */
  onCreated: (booking: ClubBooking) => void;
};

/** Why a slot of the preview cannot be chosen, in words; null when it is free. A slot that has started is not offered either. */
function unavailableReason(slot: PreviewSlot, nowMs: number): string | null {
  if (slot.booked) return "Reservado";
  if (slot.blocked) return "Bloqueado";
  if (new Date(slot.startsAt).getTime() <= nowMs) return "Já começou";
  return null;
}

/**
 * US13: the owner books a slot for a customer who called or walked in. Pitch, date (today to 30 days ahead, Lisbon), a
 * free slot from the owner's slot preview (booked, blocked and started slots cannot be chosen), and the customer's name
 * and phone. The API decides in the end: on a conflict (409) or a slot that is gone (422) the message is shown, the choice
 * is cleared and the slots are loaded again. The customer has no account, so no email is sent.
 */
export function NewBookingForm({ clubId, pitches, prefill, onClose, onCreated }: NewBookingFormProps) {
  const queryClient = useQueryClient();
  const [range] = useState(() => previewDateRange());
  const [formError, setFormError] = useState<string | null>(null);
  const [now] = useState(() => Date.now());
  const hint = useRef(prefill.startsAt);

  const initialPitch = pitches.find((pitch) => pitch.id === prefill.pitchId)?.id ?? pitches[0]?.id ?? "";
  const initialDate = prefill.date && isDateKey(prefill.date) && prefill.date >= range.min && prefill.date <= range.max ? prefill.date : range.min;
  const form = useForm<NewBookingValues, unknown, NewBookingData>({
    resolver: zodResolver(newBookingSchema),
    defaultValues: { pitchId: initialPitch, date: initialDate, startsAt: "", customerName: "", customerPhone: "" },
  });
  const { errors } = form.formState;
  const pitchId = useWatch({ control: form.control, name: "pitchId" });
  const date = useWatch({ control: form.control, name: "date" });
  const startsAt = useWatch({ control: form.control, name: "startsAt" });
  const dateInWindow = isDateKey(date) && date >= range.min && date <= range.max;

  const slots = useQuery({
    queryKey: slotsQueryKey(clubId, pitchId, date),
    queryFn: () => fetchSlots(clubId, pitchId, date),
    enabled: pitchId !== "" && dateInWindow,
  });

  // The slot the owner clicked in the calendar, chosen once the slots are there (and only if it is free).
  const slotList = slots.data?.slots;
  useEffect(() => {
    if (!hint.current || !slotList) return;
    const target = new Date(hint.current).getTime();
    hint.current = undefined;
    const slot = slotList.find((candidate) => new Date(candidate.startsAt).getTime() <= target && target < new Date(candidate.endsAt).getTime());
    if (slot && unavailableReason(slot, Date.now()) === null) {
      form.setValue("startsAt", slot.startsAt, { shouldValidate: false });
    }
  }, [slotList, form]);

  const create = useMutation({
    mutationFn: (values: NewBookingData) =>
      createClubBooking(clubId, { pitchId: values.pitchId, startsAt: values.startsAt, customerName: values.customerName, customerPhone: values.customerPhone }),
    onSuccess: async (booking) => {
      // The calendar shows it now, and the pitch's own preview marks the slot as booked.
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: calendarQueryKey(clubId), refetchType: "all" }),
        queryClient.invalidateQueries({ queryKey: slotsQueryKey(clubId, booking.pitchId), refetchType: "all" }),
      ]);
      onCreated(booking);
    },
    onError: async (error) => {
      if (!isExpectedApiError(error)) {
        toast.error(unexpectedErrorMessage(error));
        return;
      }
      if (error.code === "validation_failed" && showApiFieldErrors(form, error.fieldErrors, FIELDS)) {
        return;
      }
      if (REFRESH_CODES.has(error.code) || error.status === 409) {
        setFormError(errorMessage(error.code));
        form.setValue("startsAt", "");
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: slotsQueryKey(clubId, pitchId), refetchType: "all" }),
          queryClient.invalidateQueries({ queryKey: calendarQueryKey(clubId), refetchType: "all" }),
        ]);
        return;
      }
      toast.error(errorMessage(error.code));
    },
  });

  const choose = (field: "pitchId" | "date", value: string) => {
    setFormError(null);
    form.setValue(field, value, { shouldValidate: form.formState.isSubmitted });
    form.setValue("startsAt", "");
  };

  const slotsId = "new-booking-slots";

  return (
    <form
      noValidate
      aria-label="Nova reserva"
      className="flex flex-col gap-4 rounded-lg border p-4"
      onSubmit={form.handleSubmit((values) => {
        setFormError(null);
        create.mutate(values);
      })}
    >
      <div className="flex items-start justify-between gap-3">
        <h3 className="font-semibold">Nova reserva para um cliente</h3>
        <Button type="button" variant="ghost" size="sm" onClick={onClose}>
          Fechar
        </Button>
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <FormField id="new-booking-pitch" label="Campo" error={errors.pitchId?.message}>
          <NativeSelect id="new-booking-pitch" value={pitchId} onChange={(event) => choose("pitchId", event.target.value)}>
            {pitches.map((pitch) => (
              <option key={pitch.id} value={pitch.id}>
                {pitch.name} · {sportLabel(pitch.sport)}
              </option>
            ))}
          </NativeSelect>
        </FormField>

        <FormField id="new-booking-date" label="Data" error={errors.date?.message ?? (dateInWindow ? undefined : DATE_OUT_OF_WINDOW)}>
          <Input
            id="new-booking-date"
            type="date"
            min={range.min}
            max={range.max}
            value={date}
            aria-invalid={dateInWindow ? undefined : true}
            onChange={(event) => choose("date", event.target.value)}
          />
        </FormField>
      </div>

      <div className="flex flex-col gap-2">
        <span id={`${slotsId}-label`} className="text-sm font-medium">
          Horário
        </span>
        {!dateInWindow ? null : slots.isPending ? (
          <p role="status" className="text-sm text-muted-foreground">
            A carregar os horários…
          </p>
        ) : slots.isError ? (
          <div role="alert" className="flex flex-col items-start gap-2">
            <p className="text-sm">{errorMessage(slots.error instanceof ApiError ? slots.error.code : "unknown_error")}</p>
            <Button type="button" variant="outline" size="sm" onClick={() => void slots.refetch()}>
              Tentar novamente
            </Button>
          </div>
        ) : slots.data.slots.length === 0 ? (
          <p className="text-sm text-muted-foreground">Sem horários neste dia: o campo está fechado.</p>
        ) : (
          <ul aria-labelledby={`${slotsId}-label`} className="flex flex-wrap gap-2">
            {slots.data.slots.map((slot) => {
              const reason = unavailableReason(slot, now);
              const label = `${formatLisbonSlotTime(slot.startsAt)} – ${formatLisbonSlotTime(slot.endsAt)}`;
              return (
                <li key={slot.startsAt}>
                  <button
                    type="button"
                    disabled={reason !== null}
                    aria-pressed={startsAt === slot.startsAt}
                    onClick={() => {
                      setFormError(null);
                      form.setValue("startsAt", slot.startsAt, { shouldValidate: form.formState.isSubmitted });
                    }}
                    className={`rounded-md border px-3 py-1 text-sm tabular-nums disabled:cursor-not-allowed disabled:border-dashed disabled:bg-muted disabled:text-muted-foreground ${
                      startsAt === slot.startsAt ? "border-foreground bg-foreground text-background" : "hover:bg-accent"
                    }`}
                  >
                    {label} · {formatPrice(slot.price)}
                    {reason ? (
                      <>
                        {" "}
                        <span className="ml-1 text-xs">{reason}</span>
                      </>
                    ) : null}
                  </button>
                </li>
              );
            })}
          </ul>
        )}
        {errors.startsAt ? (
          <p id={`${slotsId}-error`} className="text-sm text-destructive">
            {errors.startsAt.message}
          </p>
        ) : null}
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <FormField id="new-booking-name" label="Nome do cliente" hint={`Até ${CUSTOMER_NAME_MAX_LENGTH} caracteres.`} error={errors.customerName?.message}>
          <Input
            id="new-booking-name"
            autoComplete="off"
            aria-invalid={errors.customerName ? true : undefined}
            aria-describedby={errors.customerName ? "new-booking-name-error" : "new-booking-name-hint"}
            {...form.register("customerName", { onChange: () => setFormError(null) })}
          />
        </FormField>
        <FormField id="new-booking-phone" label="Telemóvel do cliente" hint="Por exemplo 912 345 678 ou +351 912 345 678." error={errors.customerPhone?.message}>
          <Input
            id="new-booking-phone"
            type="tel"
            autoComplete="off"
            aria-invalid={errors.customerPhone ? true : undefined}
            aria-describedby={errors.customerPhone ? "new-booking-phone-error" : "new-booking-phone-hint"}
            {...form.register("customerPhone", { onChange: () => setFormError(null) })}
          />
        </FormField>
      </div>

      {formError ? (
        <p role="alert" className="rounded-md border border-destructive/40 bg-destructive/10 px-3 py-2 text-sm text-destructive">
          {formError}
        </p>
      ) : null}

      <p className="text-sm text-muted-foreground">O cliente não tem conta, por isso não recebe nenhum email.</p>

      <div className="flex flex-wrap items-center gap-3">
        <Button type="submit" disabled={create.isPending}>
          {create.isPending ? "A reservar…" : "Criar reserva"}
        </Button>
        <Button type="button" variant="outline" disabled={create.isPending} onClick={onClose}>
          Cancelar
        </Button>
      </div>
    </form>
  );
}
