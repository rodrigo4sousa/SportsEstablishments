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
import { createBlock, blocksQueryKey, pitchesQueryKey, slotsQueryKey, type BlockRequest } from "../api";
import { TIME_STEP_MINUTES } from "../schedule";
import { useRestoreFocus } from "@/lib/focus";
import { BLOCK_REASON_MAX_LENGTH, blockSchema, type BlockFormData, type BlockFormValues } from "../schemas/block";

const FIELDS = ["startDate", "startTime", "endDate", "endTime", "reason"] as const satisfies ReadonlyArray<keyof BlockFormValues>;

const EMPTY: BlockFormValues = { startDate: "", startTime: "", endDate: "", endTime: "", reason: "" };

/** The API names a pair of date and time by one key (`start`, `end`); it goes on the date field of the pair. */
function formErrors(fieldErrors: Readonly<Record<string, string[]>>): Record<string, string[]> {
  const mapped: Record<string, string[]> = {};
  if (fieldErrors.start) mapped.startDate = fieldErrors.start;
  if (fieldErrors.end) mapped.endDate = fieldErrors.end;
  if (fieldErrors.reason) mapped.reason = fieldErrors.reason;
  return mapped;
}

type BlockFormProps = {
  clubId: string;
  pitchId: string;
  /** Called when the form is finished (saved, cancelled, or the pitch turned out not to exist any more). */
  onDone: () => void;
};

/**
 * US09: block a period of a pitch. Start and end are a date and a time each (Lisbon time), and may be on different
 * days. An overlap with another block is not about one field, so it shows above the buttons.
 */
export function BlockForm({ clubId, pitchId, onDone }: BlockFormProps) {
  const queryClient = useQueryClient();
  const [formError, setFormError] = useState<string | null>(null);
  const form = useForm<BlockFormValues, unknown, BlockFormData>({ resolver: zodResolver(blockSchema), defaultValues: EMPTY });
  const { errors } = form.formState;

  const save = useMutation({
    mutationFn: (request: BlockRequest) => createBlock(clubId, pitchId, request),
    onSuccess: async () => {
      // The list, and the preview, where the slots it covers are now marked.
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: blocksQueryKey(clubId, pitchId), refetchType: "all" }),
        queryClient.invalidateQueries({ queryKey: slotsQueryKey(clubId, pitchId), refetchType: "all" }),
      ]);
      toast.success("Bloqueio criado.");
      onDone();
    },
    onError: async (error) => {
      if (!isExpectedApiError(error)) {
        toast.error(unexpectedErrorMessage(error));
        return;
      }
      if (error.code === "validation_failed" && showApiFieldErrors(form, formErrors(error.fieldErrors), FIELDS)) {
        return;
      }
      if (error.code === "block_overlap") {
        setFormError(errorMessage(error.code));
        return;
      }
      toast.error(errorMessage(error.code));
      if (error.code === "pitch_not_found") {
        await queryClient.invalidateQueries({ queryKey: pitchesQueryKey(clubId), refetchType: "all" });
        onDone();
      }
    },
  });

  // UX-002: a disabled button drops focus while saving; it gets it back when the save ends.
  const submitButton = useRestoreFocus<HTMLButtonElement>(save.isPending);

  const id = (field: string) => `block-${pitchId}-${field}`;
  const message = (...fields: Array<(typeof FIELDS)[number]>) => fields.map((field) => errors[field]?.message).find(Boolean);
  const describedBy = (field: (typeof FIELDS)[number], text: string | undefined) => (text ? `${id(field)}-error` : undefined);

  const period = (part: "start" | "end", label: string) => {
    const dateField = `${part}Date` as const;
    const timeField = `${part}Time` as const;
    const text = message(dateField, timeField);
    return (
      <div className="flex flex-col gap-2">
        <span className="text-sm font-medium">{label}</span>
        <div className="flex flex-wrap gap-3">
          <div className="flex items-center gap-2">
            <Label htmlFor={id(dateField)} className="text-sm font-normal">
              Data
            </Label>
            <Input
              id={id(dateField)}
              type="date"
              className="w-44"
              aria-label={`${label}: data`}
              aria-invalid={errors[dateField] ? true : undefined}
              aria-describedby={describedBy(dateField, text)}
              {...form.register(dateField, { onChange: () => setFormError(null) })}
            />
          </div>
          <div className="flex items-center gap-2">
            <Label htmlFor={id(timeField)} className="text-sm font-normal">
              Hora
            </Label>
            <Input
              id={id(timeField)}
              type="time"
              step={TIME_STEP_MINUTES * 60}
              className="w-32"
              aria-label={`${label}: hora`}
              aria-invalid={errors[timeField] ? true : undefined}
              aria-describedby={describedBy(timeField, text)}
              {...form.register(timeField, { onChange: () => setFormError(null) })}
            />
          </div>
        </div>
        {text ? (
          <p id={`${id(errors[dateField] ? dateField : timeField)}-error`} className="text-sm text-destructive">
            {text}
          </p>
        ) : null}
      </div>
    );
  };

  return (
    <form
      noValidate
      aria-label="Criar bloqueio"
      className="flex flex-col gap-4 rounded-md border p-4"
      onSubmit={form.handleSubmit((values) => {
        setFormError(null);
        save.mutate({
          start: `${values.startDate}T${values.startTime}`,
          end: `${values.endDate}T${values.endTime}`,
          reason: values.reason === "" ? null : values.reason,
        });
      })}
    >
      {period("start", "Início")}
      {period("end", "Fim")}
      <p className="text-sm text-muted-foreground">Horas de Lisboa, de meia em meia hora. O bloqueio pode abranger vários dias.</p>

      <div className="flex flex-col gap-2">
        <Label htmlFor={id("reason")}>Motivo (opcional)</Label>
        <Input
          id={id("reason")}
          autoComplete="off"
          maxLength={BLOCK_REASON_MAX_LENGTH * 2}
          aria-invalid={errors.reason ? true : undefined}
          aria-describedby={describedBy("reason", errors.reason?.message)}
          {...form.register("reason")}
        />
        {errors.reason ? (
          <p id={`${id("reason")}-error`} className="text-sm text-destructive">
            {errors.reason.message}
          </p>
        ) : null}
      </div>

      {formError ? (
        <p role="alert" className="text-sm text-destructive">
          {formError}
        </p>
      ) : null}

      <div className="flex items-center gap-3">
        <Button ref={submitButton} type="submit" size="cta" disabled={save.isPending}>
          {save.isPending ? "A guardar…" : "Criar bloqueio"}
        </Button>
        <Button type="button" variant="ghost" onClick={onDone}>
          Cancelar
        </Button>
      </div>
    </form>
  );
}
