import { apiJson } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";
import {
  toAdminOwnerDetail,
  toAdminOwnersPage,
  toAdminStats,
  toBookingsOverTime,
  toOwnerStatusChange,
  type AdminOwnerDetail,
  type AdminOwnersPage,
  type AdminStats,
  type BookingsOverTime,
  type Granularity,
  type OwnerSort,
  type OwnerStatusChange,
  type SortDirection,
} from "./types";

export const adminQueryKey = ["admin"] as const;
export const adminStatsQueryKey = [...adminQueryKey, "stats"] as const;

export type ChartQuery = { granularity: Granularity; from: string | null; to: string | null; includeCancelled: boolean };
export type OwnersQuery = { search: string; sort: OwnerSort; direction: SortDirection; page: number };

export const bookingsOverTimeQueryKey = (query: ChartQuery) => [...adminQueryKey, "bookings-over-time", query] as const;
export const adminOwnerQueryKey = (ownerId: string) => [...adminQueryKey, "owner", ownerId] as const;
export const adminOwnerHistoryQueryKey = (ownerId: string) => [...adminQueryKey, "owner-history", ownerId] as const;
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

/** US29: one owner with the number of future confirmed bookings. 404 `owner_not_found` for an id that is not an owner's. */
export async function fetchAdminOwner(ownerId: string): Promise<AdminOwnerDetail> {
  return toAdminOwnerDetail(await apiJson<components["schemas"]["AdminOwnerDetailResponse"]>(`/api/admin/owners/${encodeURIComponent(ownerId)}`));
}

/** US29: the deactivations and reactivations of an owner, newest first. */
export async function fetchOwnerHistory(ownerId: string): Promise<OwnerStatusChange[]> {
  const response = await apiJson<components["schemas"]["OwnerStatusChangeResponse"][]>(`/api/admin/owners/${encodeURIComponent(ownerId)}/history`);
  return response.map(toOwnerStatusChange);
}

/**
 * US29: deactivates or reactivates an owner, with the reason. Failures are the API's codes: 400 `validation_failed` (field
 * `reason`), 404 `owner_not_found`, 422 `owner_already_deactivated` / `owner_not_deactivated`.
 */
export async function changeOwnerStatus(ownerId: string, action: "deactivate" | "reactivate", reason: string): Promise<AdminOwnerDetail> {
  return toAdminOwnerDetail(
    await apiJson<components["schemas"]["AdminOwnerDetailResponse"]>(`/api/admin/owners/${encodeURIComponent(ownerId)}/${action}`, {
      method: "POST",
      body: JSON.stringify({ reason }),
    }),
  );
}
