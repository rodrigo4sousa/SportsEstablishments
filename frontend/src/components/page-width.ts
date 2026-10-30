/**
 * One width per kind of page, shared by the header and the body so they line up (UX-028): `narrow` for forms and lists,
 * `wide` for the admin dashboard, whose tables need the room, `calendar` for the owner's calendar, whose six or more pitch columns
 * (176 px each) need more than `wide` gives. Only the calendar page uses it, so its header stays as narrow as the rest of the area (UX-005).
 */
export const PAGE_WIDTH = {
  narrow: "max-w-3xl",
  wide: "max-w-6xl",
  calendar: "max-w-7xl",
} as const;

export type PageWidth = keyof typeof PAGE_WIDTH;
