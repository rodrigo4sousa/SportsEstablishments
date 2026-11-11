import { z } from "zod";
import type { Translator } from "@/i18n/translator";
import { SLOT_DURATIONS, TIME_STEP_MINUTES, WEEKDAYS, type WeekdayKey } from "../schedule";

/**
 * US08 schedule rules, for immediate feedback only: the API validates again and has the last word.
 * The rules match the API's PitchScheduleRequestValidator; the messages are the catalogs' (`pitches.scheduleValidation`).
 */
const TIME = /^([01]\d|2[0-3]):([0-5]\d)$/;

function minutesOf(time: string): number | null {
  const match = TIME.exec(time);
  return match ? Number(match[1]) * 60 + Number(match[2]) : null;
}

function daySchema(t: Translator<"pitches.scheduleValidation">) {
  return z
    .object({ closed: z.boolean(), opens: z.string().trim(), closes: z.string().trim() })
    .superRefine((day, context) => {
      if (day.closed) {
        return;
      }

      const check = (field: "opens" | "closes", required: string) => {
        const value = day[field];
        if (value === "") {
          context.addIssue({ code: "custom", path: [field], message: required });
          return null;
        }
        const minutes = minutesOf(value);
        if (minutes === null) {
          context.addIssue({ code: "custom", path: [field], message: t("invalidTime") });
          return null;
        }
        if (minutes % TIME_STEP_MINUTES !== 0) {
          context.addIssue({ code: "custom", path: [field], message: t("offStep") });
          return null;
        }
        return minutes;
      };

      const opens = check("opens", t("opensRequired"));
      const closes = check("closes", t("closesRequired"));
      // 00:00 as closing is midnight at the end of the day; anything else must come after the opening.
      if (opens !== null && closes !== null && closes !== 0 && closes <= opens) {
        context.addIssue({ code: "custom", path: ["closes"], message: t("notAfterOpening") });
      }
    });
}

export function createScheduleSchema(t: Translator<"pitches.scheduleValidation">) {
  const day = daySchema(t);
  return z.object({
    // A select in the form: the text of the chosen option.
    slotDurationMinutes: z.string().refine((value) => (SLOT_DURATIONS as readonly number[]).includes(Number(value)), t("duration")),
    days: z.object(Object.fromEntries(WEEKDAYS.map((weekday) => [weekday.key, day])) as Record<WeekdayKey, typeof day>),
  });
}

export type ScheduleFormValues = z.input<ReturnType<typeof createScheduleSchema>>;
export type ScheduleFormData = z.output<ReturnType<typeof createScheduleSchema>>;
