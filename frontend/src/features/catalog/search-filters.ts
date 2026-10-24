import { isSportCode } from "@/features/pitches/sports";
import { isDateKey } from "@/lib/datetime";
import { searchQuery } from "./api";
import type { SearchFilters } from "./types";

export const NO_FILTERS: SearchFilters = { sport: "", locality: "", date: "", page: 1 };

/** The longest locality the API accepts (a club's locality has the same limit). */
export const LOCALITY_MAX_LENGTH = 100;

/** Reads the search from the URL. Anything that is not valid is dropped, so a hand-edited link still shows a search. */
export function filtersFromParams(params: URLSearchParams): SearchFilters {
  const sport = params.get("sport") ?? "";
  const locality = (params.get("locality") ?? "").trim();
  const date = params.get("date") ?? "";
  const page = Number(params.get("page") ?? "1");
  return {
    sport: isSportCode(sport) ? sport : "",
    locality: locality.length <= LOCALITY_MAX_LENGTH ? locality : "",
    // Kept even when it is out of the window, so the page can say so instead of silently showing another day.
    date: date === "" || isDateKey(date) ? date : "",
    page: Number.isInteger(page) && page >= 1 && page <= 10_000 ? page : 1,
  };
}

/** The address of a search: `/` with only the filters that are set. */
export function searchHref(pathname: string, filters: SearchFilters): string {
  const query = searchQuery(filters);
  return query ? `${pathname}?${query}` : pathname;
}

/** The link from a result to the club page; the date that was searched comes along, so the page opens on it. */
export function clubHref(slug: string, filters: Pick<SearchFilters, "date">): string {
  return filters.date ? `/clubs/${slug}?date=${encodeURIComponent(filters.date)}` : `/clubs/${slug}`;
}
