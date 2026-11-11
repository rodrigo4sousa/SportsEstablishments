"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useLocale, useTranslations } from "next-intl";
import { useMemo } from "react";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { FormField } from "@/components/form-field";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { NativeSelect } from "@/components/ui/native-select";
import { useFocusOn, useRestoreFocus } from "@/lib/focus";
import { useApiErrors } from "@/lib/api/error-messages";
import { isExpectedApiError } from "@/lib/api/expected-error";
import { showApiFieldErrors, useApiFieldMessage } from "@/lib/api/form-errors";
import { createPitch, pitchesQueryKey, updatePitch, type Pitch, type PitchRequest } from "../api";
import { PITCH_NAME_MAX_LENGTH, createPitchSchema, type PitchFormData, type PitchFormValues } from "../schemas/pitch";
import { priceInputValue } from "../price";
import { SPORTS, useSportLabel } from "../sports";
import type { AppLocale } from "@/i18n/locales";

const FIELDS = ["name", "sport", "pricePerSlot"] as const satisfies ReadonlyArray<keyof PitchFormValues>;

type FieldName = (typeof FIELDS)[number];

const NEW_PITCH: PitchFormValues = { name: "", sport: "", pricePerSlot: "" };

function valuesOf(pitch: Pitch, locale: AppLocale): PitchFormValues {
  return { name: pitch.name, sport: pitch.sport, pricePerSlot: priceInputValue(pitch.pricePerSlot, locale) };
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
  const t = useTranslations("pitches.form");
  const common = useTranslations("common");
  const validation = useTranslations("pitches.validation");
  const priceErrors = useTranslations("price.errors");
  const locale = useLocale();
  const sportLabel = useSportLabel();
  const apiErrors = useApiErrors();
  const fieldMessage = useApiFieldMessage("pitch");
  const schema = useMemo(() => createPitchSchema(validation, priceErrors), [validation, priceErrors]);
  const form = useForm<PitchFormValues, unknown, PitchFormData>({
    resolver: zodResolver(schema),
    defaultValues: pitch ? valuesOf(pitch, locale) : NEW_PITCH,
  });
  const { errors } = form.formState;
  // UX-002: the form that appears gets focus; the submit button gets it back after the save (a disabled button drops it).
  const formFocus = useFocusOn<HTMLFormElement>();

  const save = useMutation({
    mutationFn: (request: PitchRequest) => (pitch ? updatePitch(clubId, pitch.id, request) : createPitch(clubId, request)),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: pitchesQueryKey(clubId), refetchType: "all" });
      toast.success(pitch ? t("saved") : t("added"));
      onDone();
    },
    onError: async (error) => {
      if (!isExpectedApiError(error)) {
        toast.error(apiErrors.unexpected(error));
        return;
      }
      if (error.code === "validation_failed" && showApiFieldErrors(form, error.fieldErrors, FIELDS, fieldMessage)) {
        return;
      }
      toast.error(apiErrors.message(error.code));
      if (error.code === "pitch_not_found") {
        // Somebody removed it from under us: show the list as it is now.
        await queryClient.invalidateQueries({ queryKey: pitchesQueryKey(clubId), refetchType: "all" });
        onDone();
      }
    },
  });

  const submitButton = useRestoreFocus<HTMLButtonElement>(save.isPending);

  const describedBy = (field: FieldName) => (errors[field] ? `pitch-${field}-error` : undefined);
  const invalid = (field: FieldName) => (errors[field] ? true : undefined);

  return (
    <form
      noValidate
      {...formFocus}
      aria-label={pitch ? t("edit") : t("add")}
      className="flex flex-col gap-5 rounded-md border p-4"
      onSubmit={form.handleSubmit((values) => save.mutate(values))}
    >
      <FormField id="pitch-name" label={t("name")} error={errors.name?.message}>
        <Input
          id="pitch-name"
          autoComplete="off"
          maxLength={PITCH_NAME_MAX_LENGTH * 2}
          aria-invalid={invalid("name")}
          aria-describedby={describedBy("name")}
          {...form.register("name")}
        />
      </FormField>

      <FormField id="pitch-sport" label={t("sport")} error={errors.sport?.message}>
        <NativeSelect id="pitch-sport" aria-invalid={invalid("sport")} aria-describedby={describedBy("sport")} {...form.register("sport")}>
          <option value="">{t("chooseSport")}</option>
          {SPORTS.map((sport) => (
            <option key={sport.code} value={sport.code}>
              {sportLabel(sport.code)}
            </option>
          ))}
        </NativeSelect>
      </FormField>

      <FormField
        id="pitch-pricePerSlot"
        label={t("price")}
        hint={t("priceHint")}
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

      <div className="flex flex-wrap items-center gap-3">
        <Button ref={submitButton} type="submit" size="cta" disabled={save.isPending}>
          {save.isPending ? common("saving") : pitch ? t("save") : t("add")}
        </Button>
        <Button type="button" size="cta" variant="ghost" onClick={onDone}>
          {common("cancel")}
        </Button>
      </div>
    </form>
  );
}
