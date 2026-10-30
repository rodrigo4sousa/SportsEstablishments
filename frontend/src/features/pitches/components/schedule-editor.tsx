"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import { useForm, useWatch } from "react-hook-form";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { isExpectedApiError, unexpectedErrorMessage } from "@/lib/api/expected-error";
import { showApiFieldErrors } from "@/lib/api/form-errors";
import { fetchSchedule, pitchesQueryKey, saveSchedule, scheduleQueryKey, slotsQueryKey, type DayHours, type PitchSchedule } from "../api";
import { SLOT_DURATIONS, TIME_STEP_MINUTES, WEEKDAYS, type WeekdayKey } from "../schedule";
import { useRestoreFocus } from "@/lib/focus";
import { scheduleSchema, type ScheduleFormData, type ScheduleFormValues } from "../schemas/schedule";

const DEFAULT_OPENS = "09:00";
const DEFAULT_CLOSES = "22:00";

function valuesOf(schedule: PitchSchedule): ScheduleFormValues {
  const day = (hours: DayHours) => ({ closed: hours.closed, opens: hours.opens ?? "", closes: hours.closes ?? "" });
  return {
    slotDurationMinutes: String(schedule.slotDurationMinutes),
    days: Object.fromEntries(WEEKDAYS.map(({ key }) => [key, day(schedule[key])])) as ScheduleFormValues["days"],
  };
}

function requestOf(data: ScheduleFormData): PitchSchedule {
  const day = (key: WeekdayKey): DayHours =>
    data.days[key].closed ? { closed: true, opens: null, closes: null } : { closed: false, opens: data.days[key].opens, closes: data.days[key].closes };
  return {
    slotDurationMinutes: Number(data.slotDurationMinutes),
    monday: day("monday"),
    tuesday: day("tuesday"),
    wednesday: day("wednesday"),
    thursday: day("thursday"),
    friday: day("friday"),
    saturday: day("saturday"),
    sunday: day("sunday"),
  };
}

/**
 * The API's field keys (`monday.closes`, `monday`, `slotDurationMinutes`) as form paths. A message about a whole
 * day (a closed day with hours, a missing day) goes on that day's "Fechado" toggle.
 */
function formErrors(fieldErrors: Readonly<Record<string, string[]>>): Record<string, string[]> {
  const mapped: Record<string, string[]> = {};
  for (const [key, messages] of Object.entries(fieldErrors)) {
    const [day, field] = key.split(".");
    if (key === "slotDurationMinutes") {
      mapped[key] = messages;
    } else if ((WEEKDAYS as readonly { key: string }[]).some((weekday) => weekday.key === day)) {
      mapped[`days.${day}.${field ?? "closed"}`] = messages;
    }
  }
  return mapped;
}

const FIELDS = [
  "slotDurationMinutes",
  ...WEEKDAYS.flatMap(({ key }) => [`days.${key}.closed`, `days.${key}.opens`, `days.${key}.closes`] as const),
] as const;

type ScheduleFormProps = {
  clubId: string;
  pitchId: string;
  schedule: PitchSchedule;
  /** The pitch is gone (pitch_not_found): the parent closes the editor. */
  onGone: () => void;
};

function ScheduleForm({ clubId, pitchId, schedule, onGone }: ScheduleFormProps) {
  const queryClient = useQueryClient();
  const form = useForm<ScheduleFormValues, unknown, ScheduleFormData>({
    resolver: zodResolver(scheduleSchema),
    defaultValues: valuesOf(schedule),
  });
  const { errors } = form.formState;
  const days = useWatch({ control: form.control, name: "days" });

  const save = useMutation({
    mutationFn: (request: PitchSchedule) => saveSchedule(clubId, pitchId, request),
    onSuccess: async (saved) => {
      queryClient.setQueryData(scheduleQueryKey(clubId, pitchId), saved);
      form.reset(valuesOf(saved));
      // The preview shows what the schedule generates: it has to follow the new hours and slot length.
      await queryClient.invalidateQueries({ queryKey: slotsQueryKey(clubId, pitchId), refetchType: "all" });
      toast.success("Horário guardado.");
    },
    onError: async (error) => {
      if (!isExpectedApiError(error)) {
        toast.error(unexpectedErrorMessage(error));
        return;
      }
      if (error.code === "validation_failed" && showApiFieldErrors(form, formErrors(error.fieldErrors), FIELDS)) {
        return;
      }
      toast.error(errorMessage(error.code));
      if (error.code === "pitch_not_found") {
        await queryClient.invalidateQueries({ queryKey: pitchesQueryKey(clubId), refetchType: "all" });
        onGone();
      }
    },
  });

  // UX-002: a disabled button drops focus while saving; it gets it back when the save ends.
  const submitButton = useRestoreFocus<HTMLButtonElement>(save.isPending);

  return (
    <form
      noValidate
      aria-label="Horário do campo"
      className="flex flex-col gap-4"
      onSubmit={form.handleSubmit((values) => save.mutate(requestOf(values)))}
    >
      <div className="flex flex-col gap-2">
        <Label htmlFor={`slot-duration-${pitchId}`}>Duração do horário</Label>
        <NativeSelect
          id={`slot-duration-${pitchId}`}
          className="sm:max-w-48"
          aria-invalid={errors.slotDurationMinutes ? true : undefined}
          aria-describedby={errors.slotDurationMinutes ? `slot-duration-${pitchId}-error` : undefined}
          {...form.register("slotDurationMinutes")}
        >
          {SLOT_DURATIONS.map((minutes) => (
            <option key={minutes} value={minutes}>
              {minutes} minutos
            </option>
          ))}
        </NativeSelect>
        {errors.slotDurationMinutes ? (
          <p id={`slot-duration-${pitchId}-error`} className="text-sm text-destructive">
            {errors.slotDurationMinutes.message}
          </p>
        ) : null}
      </div>

      <fieldset className="flex flex-col divide-y rounded-md border">
        <legend className="sr-only">Horário de funcionamento (hora de Lisboa)</legend>
        {WEEKDAYS.map(({ key, label }) => {
          const closed = days[key].closed;
          const row = errors.days?.[key];
          const id = `${pitchId}-${key}`;
          return (
            <div key={key} role="group" aria-label={label} className="flex flex-col gap-2 px-4 py-3">
              <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
                <span className="w-32 font-medium">{label}</span>
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="size-4"
                    aria-invalid={row?.closed ? true : undefined}
                    {...form.register(`days.${key}.closed`, {
                      onChange: (event: { target: { checked: boolean } }) => {
                        // Opening a day offers usual hours to edit, so the owner is not left with two empty boxes.
                        if (!event.target.checked) {
                          if (form.getValues(`days.${key}.opens`) === "") form.setValue(`days.${key}.opens`, DEFAULT_OPENS);
                          if (form.getValues(`days.${key}.closes`) === "") form.setValue(`days.${key}.closes`, DEFAULT_CLOSES);
                        }
                      },
                    })}
                  />
                  Fechado
                </label>
                {closed ? null : (
                  <>
                    <div className="flex items-center gap-2">
                      <Label htmlFor={`${id}-opens`} className="text-sm font-normal">
                        Abre
                      </Label>
                      <Input
                        id={`${id}-opens`}
                        type="time"
                        step={TIME_STEP_MINUTES * 60}
                        className="w-32"
                        aria-label={`${label}: abertura`}
                        aria-invalid={row?.opens ? true : undefined}
                        aria-describedby={row?.opens ? `${id}-opens-error` : undefined}
                        {...form.register(`days.${key}.opens`)}
                      />
                    </div>
                    <div className="flex items-center gap-2">
                      <Label htmlFor={`${id}-closes`} className="text-sm font-normal">
                        Fecha
                      </Label>
                      <Input
                        id={`${id}-closes`}
                        type="time"
                        step={TIME_STEP_MINUTES * 60}
                        className="w-32"
                        aria-label={`${label}: fecho`}
                        aria-invalid={row?.closes ? true : undefined}
                        aria-describedby={row?.closes ? `${id}-closes-error` : undefined}
                        {...form.register(`days.${key}.closes`)}
                      />
                    </div>
                  </>
                )}
              </div>
              {row?.closed?.message ? (
                <p className="text-sm text-destructive">{row.closed.message}</p>
              ) : null}
              {row?.opens?.message ? (
                <p id={`${id}-opens-error`} className="text-sm text-destructive">
                  {row.opens.message}
                </p>
              ) : null}
              {row?.closes?.message ? (
                <p id={`${id}-closes-error`} className="text-sm text-destructive">
                  {row.closes.message}
                </p>
              ) : null}
            </div>
          );
        })}
      </fieldset>
      <p className="text-sm text-muted-foreground">Horas de Lisboa, de meia em meia hora. Fecho às 00:00 é meia-noite.</p>

      <div>
        <Button ref={submitButton} type="submit" size="cta" disabled={save.isPending}>
          {save.isPending ? "A guardar…" : "Guardar horário"}
        </Button>
      </div>
    </form>
  );
}

/** US08: loads a pitch's schedule and shows the editor for the whole week. A new pitch is closed every day. */
export function ScheduleEditor({ clubId, pitchId, onGone }: { clubId: string; pitchId: string; onGone: () => void }) {
  const schedule = useQuery({
    queryKey: scheduleQueryKey(clubId, pitchId),
    queryFn: () => fetchSchedule(clubId, pitchId),
    // The form must not be refreshed under the owner's hands while they type.
    refetchOnWindowFocus: false,
  });

  useEffect(() => {
    if (schedule.error instanceof ApiError && schedule.error.status === 404) {
      onGone();
    }
  }, [schedule.error, onGone]);

  if (schedule.isPending) {
    return (
      <p role="status" className="text-muted-foreground">
        A carregar o horário…
      </p>
    );
  }

  if (schedule.isError) {
    const code = schedule.error instanceof ApiError ? schedule.error.code : "unknown_error";
    return (
      <div role="alert" className="flex flex-col items-start gap-3">
        <p>{errorMessage(code)}</p>
        <Button type="button" variant="outline" onClick={() => void schedule.refetch()}>
          Tentar novamente
        </Button>
      </div>
    );
  }

  return <ScheduleForm clubId={clubId} pitchId={pitchId} schedule={schedule.data} onGone={onGone} />;
}
