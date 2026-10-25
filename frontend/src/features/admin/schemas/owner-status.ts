import { z } from "zod";

/**
 * US29 rules for the reason given when deactivating or reactivating an owner, for immediate feedback only: the API validates
 * again and has the last word. The messages match the API's `OwnerStatusRequestValidator`.
 */
export const OWNER_STATUS_REASON_MAX_LENGTH = 500;

export const OWNER_STATUS_MESSAGES = {
  required: "Indica o motivo.",
  tooLong: `O motivo pode ter no máximo ${OWNER_STATUS_REASON_MAX_LENGTH} caracteres.`,
} as const;

export const ownerStatusSchema = z.object({
  reason: z.string().trim().min(1, OWNER_STATUS_MESSAGES.required).max(OWNER_STATUS_REASON_MAX_LENGTH, OWNER_STATUS_MESSAGES.tooLong),
});

export type OwnerStatusValues = z.input<typeof ownerStatusSchema>;
export type OwnerStatusData = z.output<typeof ownerStatusSchema>;
