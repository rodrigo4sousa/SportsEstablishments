import { apiJson } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";
import { toPublicSlots, type ClubSearchPage, type PublicSlots, type SearchFilters } from "./types";

/** Everything the public pages read sits under this key, so one invalidation refreshes searches and slots alike. */
export const catalogQueryKey = ["catalog"] as const;
export const searchQueryKey = (filters: SearchFilters) => [...catalogQueryKey, "search", filters] as const;
export const localitiesQueryKey = [...catalogQueryKey, "localities"] as const;
export const publicSlotsQueryKey = (slug: string, pitchId: string, date?: string) =>
  date === undefined
    ? ([...catalogQueryKey, "slots", slug] as const)
    : ([...catalogQueryKey, "slots", slug, pitchId, date] as const);

/** The query string of the search: only the filters that are set. */
export function searchQuery(filters: SearchFilters): string {
  const params = new URLSearchParams();
  if (filters.sport) params.set("sport", filters.sport);
  if (filters.locality) params.set("locality", filters.locality);
  if (filters.date) params.set("date", filters.date);
  if (filters.page > 1) params.set("page", String(filters.page));
  return params.toString();
}

/** US16: the clubs that can take bookings, 20 per page. Public, no session needed. */
export async function searchClubs(filters: SearchFilters): Promise<ClubSearchPage> {
  const query = searchQuery(filters);
  const response = await apiJson<components["schemas"]["PagedResponseOfClubSearchItem"]>(`/api/clubs${query ? `?${query}` : ""}`);
  return {
    items: response.items,
    page: Number(response.page),
    pageSize: Number(response.pageSize),
    totalCount: Number(response.totalCount),
  };
}

/** US16: the localities of the clubs that can take bookings, for the select. */
export function fetchLocalities(): Promise<string[]> {
  return apiJson<string[]>("/api/localities");
}

/** US17: every slot of a pitch on a local date ("YYYY-MM-DD"), with one `available` flag each. */
export async function fetchPublicSlots(slug: string, pitchId: string, date: string): Promise<PublicSlots> {
  return toPublicSlots(
    await apiJson<components["schemas"]["PublicSlotsResponse"]>(
      `/api/clubs/${encodeURIComponent(slug)}/pitches/${encodeURIComponent(pitchId)}/slots?date=${encodeURIComponent(date)}`,
    ),
  );
}
