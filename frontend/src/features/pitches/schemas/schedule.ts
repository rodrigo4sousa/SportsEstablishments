import { z } from "zod";
import { SLOT_DURATIONS, TIME_STEP_MINUTES, WEEKDAYS, type WeekdayKey } from "../schedule";

/**
 * US08 schedule rules, for immediate feedback only: the API validates again and has the last word.
 * Messages match the API's PitchScheduleRequestValidator.
 */
export const SCHEDULE_MESSAGES = {
  duration: "A duração do horário tem de ser 30, 60 ou 90 minutos.",
  opensRequired: "Indica a hora de abertura.",
  closesRequired: "Indica a hora de fecho.",
  invalidTime: "Indica a hora no formato HH:mm, por exemplo 09:00.",
  offStep: "As horas vão de meia em meia hora, por exemplo 09:00 ou 09:30.",
  notAfterOpening:
    "A hora de fecho tem de ser depois da abertura. O fecho mais tarde é 00:00 (meia-noite); horários que passam da meia-noite não são aceites.",
} as const;

const TIME = /^([01]\d|2[0-3]):([0-5]\d)$/;

function minutesOf(time: string): number | null {
  const match = TIME.exec(time);
  return match ? Number(match[1]) * 60 + Number(match[2]) : null;
}

const daySchema = z
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
        context.addIssue({ code: "custom", path: [field], message: SCHEDULE_MESSAGES.invalidTime });
        return null;
      }
      if (minutes % TIME_STEP_MINUTES !== 0) {
        context.addIssue({ code: "custom", path: [field], message: SCHEDULE_MESSAGES.offStep });
        return null;
      }
      return minutes;
    };

    const opens = check("opens", SCHEDULE_MESSAGES.opensRequired);
    const closes = check("closes", SCHEDULE_MESSAGES.closesRequired);
    // 00:00 as closing is midnight at the end of the day; anything else must come after the opening.
    if (opens !== null && closes !== null && closes !== 0 && closes <= opens) {
      context.addIssue({ code: "custom", path: ["closes"], message: SCHEDULE_MESSAGES.notAfterOpening });
    }
  });

export const scheduleSchema = z.object({
  // A select in the form: the text of the chosen option.
  slotDurationMinutes: z.string().refine((value) => (SLOT_DURATIONS as readonly number[]).includes(Number(value)), SCHEDULE_MESSAGES.duration),
  days: z.object(Object.fromEntries(WEEKDAYS.map((day) => [day.key, daySchema])) as Record<WeekdayKey, typeof daySchema>),
});

export type ScheduleFormValues = z.input<typeof scheduleSchema>;
export type ScheduleFormData = z.output<typeof scheduleSchema>;
