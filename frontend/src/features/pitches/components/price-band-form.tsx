"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { errorMessage } from "@/lib/api/error-messages";
import { isExpectedApiError, unexpectedErrorMessage } from "@/lib/api/expected-error";
import { showApiFieldErrors } from "@/lib/api/form-errors";
import {
  createPriceBand,
  pitchesQueryKey,
  priceBandsQueryKey,
  slotsQueryKey,
  updatePriceBand,
  type PriceBand,
  type PriceBandRequest,
} from "../api";
import { priceInputValue } from "../price";
import { TIME_STEP_MINUTES, WEEKDAYS } from "../schedule";
import { useRestoreFocus } from "@/lib/focus";
import { priceBandSchema, type PriceBandFormData, type PriceBandFormValues } from "../schemas/price-band";

const FIELDS = ["days", "start", "end", "price"] as const satisfies ReadonlyArray<keyof PriceBandFormValues>;

type PriceBandFormProps = {
  clubId: string;
  pitchId: string;
  /** The band being edited; without it the form adds one. */
  band?: PriceBand;
  /** Called when the form is finished (saved, cancelled, or the pitch turned out not to exist any more). */
  onDone: () => void;
};

/**
 * US10: add or edit a price band: weekdays, start and end (club time, 30-minute steps; an end of 00:00 is midnight) and
 * the price per slot. An overlap with another band is not about one field, so it shows above the buttons.
 */
export function PriceBandForm({ clubId, pitchId, band, onDone }: PriceBandFormProps) {
  const queryClient = useQueryClient();
  const [formError, setFormError] = useState<string | null>(null);
  const form = useForm<PriceBandFormValues, unknown, PriceBandFormData>({
    resolver: zodResolver(priceBandSchema),
    defaultValues: band
      ? { days: band.days, start: band.start, end: band.end, price: priceInputValue(band.price) }
      : { days: [], start: "", end: "", price: "" },
  });
  const { errors } = form.formState;

  const save = useMutation({
    mutationFn: (request: PriceBandRequest) => (band ? updatePriceBand(clubId, pitchId, band.id, request) : createPriceBand(clubId, pitchId, request)),
    onSuccess: async () => {
      // The list, and the preview, where the slots now show their new price.
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: priceBandsQueryKey(clubId, pitchId), refetchType: "all" }),
        queryClient.invalidateQueries({ queryKey: slotsQueryKey(clubId, pitchId), refetchType: "all" }),
      ]);
      toast.success(band ? "Faixa guardada." : "Faixa criada.");
      onDone();
    },
    onError: async (error) => {
      if (!isExpectedApiError(error)) {
        toast.error(unexpectedErrorMessage(error));
        return;
      }
      if (error.code === "validation_failed" && showApiFieldErrors(form, error.fieldErrors, FIELDS)) {
        return;
      }
      if (error.code === "price_band_overlap" || error.code === "price_band_limit_reached") {
        setFormError(errorMessage(error.code));
        return;
      }
      toast.error(errorMessage(error.code));
      if (error.code === "pitch_not_found") {
        await queryClient.invalidateQueries({ queryKey: pitchesQueryKey(clubId), refetchType: "all" });
        onDone();
      } else if (error.code === "price_band_not_found") {
        await queryClient.invalidateQueries({ queryKey: priceBandsQueryKey(clubId, pitchId), refetchType: "all" });
        onDone();
      }
    },
  });

  // UX-002: a disabled button drops focus while saving; it gets it back when the save ends.
  const submitButton = useRestoreFocus<HTMLButtonElement>(save.isPending);

  const id = (field: string) => `price-band-${band?.id ?? "new"}-${field}`;
  const describedBy = (field: (typeof FIELDS)[number]) => (errors[field] ? `${id(field)}-error` : undefined);
  const clearFormError = () => setFormError(null);
  const fieldError = (field: (typeof FIELDS)[number]) =>
    errors[field] ? (
      <p id={`${id(field)}-error`} className="text-sm text-destructive">
        {errors[field]?.message}
      </p>
    ) : null;

  return (
    <form
      noValidate
      aria-label={band ? "Editar faixa de preço" : "Criar faixa de preço"}
      className="flex flex-col gap-4 rounded-md border p-4"
      onSubmit={form.handleSubmit((values) => {
        setFormError(null);
        save.mutate({ days: values.days, start: values.start, end: values.end, price: values.price });
      })}
    >
      <fieldset className="flex flex-col gap-2" aria-describedby={describedBy("days")} aria-invalid={errors.days ? true : undefined}>
        <legend className="text-sm font-medium">Dias da semana</legend>
        <div className="flex flex-wrap gap-x-4 gap-y-2">
          {WEEKDAYS.map((day) => (
            <label key={day.key} className="flex items-center gap-2 text-sm">
              <input type="checkbox" value={day.key} {...form.register("days", { onChange: clearFormError })} />
              {day.label}
            </label>
          ))}
        </div>
        {fieldError("days")}
      </fieldset>

      <div className="flex flex-wrap gap-4">
        {(["start", "end"] as const).map((field) => (
          <div key={field} className="flex flex-col gap-2">
            <Label htmlFor={id(field)}>{field === "start" ? "Início" : "Fim"}</Label>
            <Input
              id={id(field)}
              type="time"
              step={TIME_STEP_MINUTES * 60}
              className="w-32"
              aria-invalid={errors[field] ? true : undefined}
              aria-describedby={describedBy(field)}
              {...form.register(field, { onChange: clearFormError })}
            />
            {fieldError(field)}
          </div>
        ))}
        <div className="flex flex-col gap-2">
          <Label htmlFor={id("price")}>Preço por horário (€)</Label>
          <Input
            id={id("price")}
            inputMode="decimal"
            autoComplete="off"
            className="w-32"
            aria-invalid={errors.price ? true : undefined}
            aria-describedby={describedBy("price")}
            {...form.register("price", { onChange: clearFormError })}
          />
          {fieldError("price")}
        </div>
      </div>
      <p className="text-sm text-muted-foreground">
        Horas do clube, de meia em meia hora. Vale para os horários que começam dentro da faixa; o fim 00:00 é a meia-noite.
      </p>

      {formError ? (
        <p role="alert" className="text-sm text-destructive">
          {formError}
        </p>
      ) : null}

      <div className="flex items-center gap-3">
        <Button ref={submitButton} type="submit" size="cta" disabled={save.isPending}>
          {save.isPending ? "A guardar…" : band ? "Guardar faixa" : "Criar faixa"}
        </Button>
        <Button type="button" variant="ghost" onClick={onDone}>
          Cancelar
        </Button>
      </div>
    </form>
  );
}
