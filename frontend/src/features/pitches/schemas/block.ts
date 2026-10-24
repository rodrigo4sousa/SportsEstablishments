import { z } from "zod";
import { lisbonNowLocal } from "@/lib/datetime";
import { TIME_STEP_MINUTES } from "../schedule";

/**
 * US09 block rules, for immediate feedback only: the API validates again and has the last word (the 30-minute step,
 * the 90-day limit and the 200-character reason are provisional, see US09). Messages match the API's BlockCheckValidator.
 */
export const BLOCK_REASON_MAX_LENGTH = 200;

export const BLOCK_MESSAGES = {
  startRequired: "Indica o início do bloqueio.",
  endRequired: "Indica o fim do bloqueio.",
  invalidDate: "Indica uma data válida.",
  invalidTime: "Indica a hora no formato HH:mm, por exemplo 09:00.",
  offStep: "As horas vão de meia em meia hora, por exemplo 09:00 ou 09:30.",
  past: "O início não pode estar no passado.",
  endNotAfterStart: "O fim tem de ser depois do início.",
  reason: `O motivo pode ter no máximo ${BLOCK_REASON_MAX_LENGTH} caracteres.`,
} as const;

const DATE = /^\d{4}-\d{2}-\d{2}$/;
const TIME = /^([01]\d|2[0-3]):([0-5]\d)$/;

type Part = "start" | "end";

export function createBlockSchema(now: () => Date = () => new Date()) {
  return z
    .object({
      startDate: z.string().trim(),
      startTime: z.string().trim(),
      endDate: z.string().trim(),
      endTime: z.string().trim(),
      reason: z.string().trim().max(BLOCK_REASON_MAX_LENGTH, BLOCK_MESSAGES.reason),
    })
    .superRefine((values, context) => {
      const ok: Record<Part, boolean> = { start: false, end: false };
      for (const part of ["start", "end"] as const) {
        const date = values[`${part}Date`];
        const time = values[`${part}Time`];
        const required = part === "start" ? BLOCK_MESSAGES.startRequired : BLOCK_MESSAGES.endRequired;
        if (date === "") {
          context.addIssue({ code: "custom", path: [`${part}Date`], message: required });
        } else if (!DATE.test(date) || Number.isNaN(Date.parse(`${date}T00:00:00Z`))) {
          context.addIssue({ code: "custom", path: [`${part}Date`], message: BLOCK_MESSAGES.invalidDate });
        }
        if (time === "") {
          context.addIssue({ code: "custom", path: [`${part}Time`], message: date === "" ? required : `Indica a hora ${part === "start" ? "de início" : "de fim"}.` });
        } else if (!TIME.test(time)) {
          context.addIssue({ code: "custom", path: [`${part}Time`], message: BLOCK_MESSAGES.invalidTime });
        } else if (Number(time.slice(3)) % TIME_STEP_MINUTES !== 0) {
          context.addIssue({ code: "custom", path: [`${part}Time`], message: BLOCK_MESSAGES.offStep });
        } else {
          ok[part] = date !== "" && DATE.test(date) && !Number.isNaN(Date.parse(`${date}T00:00:00Z`));
        }
      }

      const start = `${values.startDate}T${values.startTime}`;
      const end = `${values.endDate}T${values.endTime}`;
      // Local "YYYY-MM-DDTHH:mm" strings compare in time order. (Around the October clock change the API has the last word.)
      if (ok.start && start < lisbonNowLocal(now())) {
        context.addIssue({ code: "custom", path: ["startDate"], message: BLOCK_MESSAGES.past });
      }
      if (ok.start && ok.end && end <= start) {
        context.addIssue({ code: "custom", path: ["endDate"], message: BLOCK_MESSAGES.endNotAfterStart });
      }
    });
}

export const blockSchema = createBlockSchema();

export type BlockFormValues = z.input<typeof blockSchema>;
export type BlockFormData = z.output<typeof blockSchema>;
