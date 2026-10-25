import { apiJson } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";
import {
  toAdminOwnersPage,
  toAdminStats,
  toBookingsOverTime,
  type AdminOwnersPage,
  type AdminStats,
  type BookingsOverTime,
  type Granularity,
  type OwnerSort,
  type SortDirection,
} from "./types";

export const adminQueryKey = ["admin"] as const;
export const adminStatsQueryKey = [...adminQueryKey, "stats"] as const;

export type ChartQuery = { granularity: Granularity; from: string | null; to: string | null; includeCancelled: boolean };
export type OwnersQuery = { search: string; sort: OwnerSort; direction: SortDirection; page: number };

export const bookingsOverTimeQueryKey = (query: ChartQuery) => [...adminQueryKey, "bookings-over-time", query] as const;
export const adminOwnersQueryKey = (query: OwnersQuery) => [...adminQueryKey, "owners", query] as const;

/** US24: the platform's totals. */
export async function fetchAdminStats(): Promise<AdminStats> {
  return toAdminStats(await apiJson<components["schemas"]["AdminStatsResponse"]>("/api/admin/stats"));
}

/** US25: bookings per period by the Lisbon date they were made. Without `from`/`to` the API answers the last 30 days. */
export async function fetchBookingsOverTime(query: ChartQuery): Promise<BookingsOverTime> {
  const params = new URLSearchParams({ granularity: query.granularity });
  if (query.from) params.set("from", query.from);
  if (query.to) params.set("to", query.to);
  if (query.includeCancelled) params.set("includeCancelled", "true");
  return toBookingsOverTime(await apiJson<components["schemas"]["BookingsOverTimeResponse"]>(`/api/admin/bookings-over-time?${params.toString()}`));
}

/** US26: the owners with their numbers, 20 to a page. */
export async function fetchAdminOwners(query: OwnersQuery): Promise<AdminOwnersPage> {
  const params = new URLSearchParams({ sort: query.sort, direction: query.direction });
  if (query.search) params.set("search", query.search);
  if (query.page > 1) params.set("page", String(query.page));
  return toAdminOwnersPage(await apiJson<components["schemas"]["PagedResponseOfAdminOwnerResponse"]>(`/api/admin/owners?${params.toString()}`));
}
