import type { components } from "@/lib/api/schema";

/** What the public club page needs: the generated types allow numbers as strings (JSON number formats); the API sends numbers. */
export type PublicPitch = Omit<components["schemas"]["PublicPitchResponse"], "pricePerSlot"> & { pricePerSlot: number };

export type PublicClub = Omit<components["schemas"]["PublicClubResponse"], "pitches"> & { pitches: PublicPitch[] };

export function toPublicClub(club: components["schemas"]["PublicClubResponse"]): PublicClub {
  return { ...club, pitches: club.pitches.map((pitch) => ({ ...pitch, pricePerSlot: Number(pitch.pricePerSlot) })) };
}

/** A slot on the club page: `available` is the only thing the API says about its state. */
export type PublicSlot = { startsAt: string; endsAt: string; price: number; available: boolean };

export type PublicSlots = { date: string; slots: PublicSlot[] };

export function toPublicSlots(response: components["schemas"]["PublicSlotsResponse"]): PublicSlots {
  return { date: response.date, slots: response.slots.map((slot) => ({ ...slot, price: Number(slot.price) })) };
}

export type ClubSearchItem = components["schemas"]["ClubSearchItem"];

export type ClubSearchPage = { items: ClubSearchItem[]; page: number; pageSize: number; totalCount: number };

/** The search as the URL holds it; an empty string means "no filter" (and for the date, today). */
export type SearchFilters = { sport: string; locality: string; date: string; page: number };
