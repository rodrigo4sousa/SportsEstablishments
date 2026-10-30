/**
 * One width per kind of page, shared by the header and the body so they line up (UX-028): `narrow` for forms and lists,
 * `wide` for the admin dashboard, whose tables need the room. The calendar gets its own width in a later batch.
 */
export const PAGE_WIDTH = {
  narrow: "max-w-3xl",
  wide: "max-w-6xl",
} as const;

export type PageWidth = keyof typeof PAGE_WIDTH;
