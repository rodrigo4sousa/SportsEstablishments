"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { FormField } from "@/components/form-field";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { NativeSelect } from "@/components/ui/native-select";
import { errorMessage } from "@/lib/api/error-messages";
import { isExpectedApiError, unexpectedErrorMessage } from "@/lib/api/expected-error";
import { showApiFieldErrors } from "@/lib/api/form-errors";
import { createPitch, pitchesQueryKey, updatePitch, type Pitch, type PitchRequest } from "../api";
import { PITCH_NAME_MAX_LENGTH, pitchSchema, type PitchFormData, type PitchFormValues } from "../schemas/pitch";
import { priceInputValue } from "../price";
import { SPORTS } from "../sports";

const FIELDS = ["name", "sport", "pricePerSlot"] as const satisfies ReadonlyArray<keyof PitchFormValues>;

type FieldName = (typeof FIELDS)[number];

const NEW_PITCH: PitchFormValues = { name: "", sport: "", pricePerSlot: "" };

function valuesOf(pitch: Pitch): PitchFormValues {
  return { name: pitch.name, sport: pitch.sport, pricePerSlot: priceInputValue(pitch.pricePerSlot) };
}

type PitchFormProps = {
  clubId: string;
  /** The pitch being edited; without it the form adds a new one. */
  pitch?: Pitch;
  /** Called when the form is finished (saved, cancelled, or the pitch turned out not to exist any more). */
  onDone: () => void;
};

/**
 * US07: add or edit a pitch of a club. The status is not part of the form (deactivate and reactivate are their own
 * actions). A duplicate name comes back from the API as an error on the name field.
 */
export function PitchForm({ clubId, pitch, onDone }: PitchFormProps) {
  const queryClient = useQueryClient();
  const form = useForm<PitchFormValues, unknown, PitchFormData>({
    resolver: zodResolver(pitchSchema),
    defaultValues: pitch ? valuesOf(pitch) : NEW_PITCH,
  });
  const { errors } = form.formState;

  const save = useMutation({
    mutationFn: (request: PitchRequest) => (pitch ? updatePitch(clubId, pitch.id, request) : createPitch(clubId, request)),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: pitchesQueryKey(clubId), refetchType: "all" });
      toast.success(pitch ? "Alterações guardadas." : "Campo adicionado.");
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
      toast.error(errorMessage(error.code));
      if (error.code === "pitch_not_found") {
        // Somebody removed it from under us: show the list as it is now.
        await queryClient.invalidateQueries({ queryKey: pitchesQueryKey(clubId), refetchType: "all" });
        onDone();
      }
    },
  });

  const describedBy = (field: FieldName) => (errors[field] ? `pitch-${field}-error` : undefined);
  const invalid = (field: FieldName) => (errors[field] ? true : undefined);

  return (
    <form
      noValidate
      aria-label={pitch ? "Editar campo" : "Adicionar campo"}
      className="flex flex-col gap-5 rounded-md border p-4"
      onSubmit={form.handleSubmit((values) => save.mutate(values))}
    >
      <FormField id="pitch-name" label="Nome do campo" error={errors.name?.message}>
        <Input
          id="pitch-name"
          autoComplete="off"
          maxLength={PITCH_NAME_MAX_LENGTH * 2}
          aria-invalid={invalid("name")}
          aria-describedby={describedBy("name")}
          {...form.register("name")}
        />
      </FormField>

      <FormField id="pitch-sport" label="Desporto" error={errors.sport?.message}>
        <NativeSelect id="pitch-sport" aria-invalid={invalid("sport")} aria-describedby={describedBy("sport")} {...form.register("sport")}>
          <option value="">Escolhe o desporto</option>
          {SPORTS.map((sport) => (
            <option key={sport.code} value={sport.code}>
              {sport.label}
            </option>
          ))}
        </NativeSelect>
      </FormField>

      <FormField
        id="pitch-pricePerSlot"
        label="Preço por slot (€)"
        hint="Por exemplo 12,50."
        error={errors.pricePerSlot?.message}
      >
        <Input
          id="pitch-pricePerSlot"
          inputMode="decimal"
          autoComplete="off"
          aria-invalid={invalid("pricePerSlot")}
          aria-describedby={["pitch-pricePerSlot-hint", describedBy("pricePerSlot")].filter(Boolean).join(" ")}
          {...form.register("pricePerSlot")}
        />
      </FormField>

      <div className="flex items-center gap-3">
        <Button type="submit" disabled={save.isPending}>
          {save.isPending ? "A guardar…" : pitch ? "Guardar campo" : "Adicionar campo"}
        </Button>
        <Button type="button" variant="ghost" onClick={onDone}>
          Cancelar
        </Button>
      </div>
    </form>
  );
}
