import { isDateKey } from "@/lib/datetime";
import type { ChartQuery, OwnersQuery } from "./api";
import type { Granularity, OwnerSort, RegistrationsGranularity } from "./types";

/**
 * The admin page keeps its filters in the URL, so a reload or a shared link shows the same view: the chart
 * (`granularity`, `from`, `to`, `cancelled=1`; `from` and `to` are shared with the registrations and the top of the clubs, and `registrations=month` is the registrations' own grouping) and the owners table (`search`, `sort`, `direction`, `page`).
 * Values equal to the default are left out; a value that makes no sense falls back to the default.
 */
export const OWNER_SORTS: readonly OwnerSort[] = ["name", "email", "registeredAt", "status", "clubs", "pitches", "bookings", "players"];
const GRANULARITIES: readonly Granularity[] = ["day", "week", "month"];

export const DEFAULT_CHART: ChartQuery = { granularity: "day", from: null, to: null, includeCancelled: false };
export const DEFAULT_OWNERS: OwnersQuery = { search: "", sort: "name", direction: "asc", page: 1 };

/** The registrations chart groups by week (the default, left out of the URL) or month: `?registrations=month`. */
export function registrationsFromParams(params: URLSearchParams): RegistrationsGranularity {
  return params.get("registrations") === "month" ? "month" : "week";
}

export function chartFromParams(params: URLSearchParams): ChartQuery {
  const granularity = params.get("granularity");
  const from = params.get("from");
  const to = params.get("to");
  return {
    granularity: GRANULARITIES.find((value) => value === granularity) ?? DEFAULT_CHART.granularity,
    from: from !== null && isDateKey(from) ? from : null,
    to: to !== null && isDateKey(to) ? to : null,
    includeCancelled: params.get("cancelled") === "1",
  };
}

export function ownersFromParams(params: URLSearchParams): OwnersQuery {
  const page = params.get("page");
  return {
    search: (params.get("search") ?? "").trim().slice(0, 100),
    sort: OWNER_SORTS.find((value) => value === params.get("sort")) ?? DEFAULT_OWNERS.sort,
    direction: params.get("direction") === "desc" ? "desc" : "asc",
    page: page !== null && /^\d{1,5}$/.test(page) && Number(page) >= 1 ? Number(page) : 1,
  };
}

/** The whole query string for both parts of the page. */
export function adminHref(pathname: string, chart: ChartQuery, owners: OwnersQuery, registrations: RegistrationsGranularity = "week"): string {
  const params = new URLSearchParams();
  if (chart.granularity !== DEFAULT_CHART.granularity) params.set("granularity", chart.granularity);
  if (chart.from) params.set("from", chart.from);
  if (chart.to) params.set("to", chart.to);
  if (chart.includeCancelled) params.set("cancelled", "1");
  if (registrations !== "week") params.set("registrations", registrations);
  if (owners.search) params.set("search", owners.search);
  if (owners.sort !== DEFAULT_OWNERS.sort) params.set("sort", owners.sort);
  if (owners.direction !== DEFAULT_OWNERS.direction) params.set("direction", owners.direction);
  if (owners.page > 1) params.set("page", String(owners.page));
  const query = params.toString();
  return query ? `${pathname}?${query}` : pathname;
}
