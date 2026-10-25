import { apiJson } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";

export type OwnerClub = Omit<components["schemas"]["OwnerClubResponse"], "cancellationWindowHours"> & {
  cancellationWindowHours: number;
};
export type ClubRequest = Omit<components["schemas"]["ClubRequest"], "cancellationWindowHours" | "notifyOnNewBooking"> & {
  cancellationWindowHours: number;
  /** US23: always sent by the form, so saving never changes it by accident (the API leaves a missing value as it is). */
  notifyOnNewBooking: boolean;
};

export type ClubPage = {
  items: OwnerClub[];
  page: number;
  pageSize: number;
  totalCount: number;
};

export const CLUBS_PAGE_SIZE = 20;

/** Everything about the owner's clubs sits under this key, so one invalidation refreshes lists and details. */
export const clubsQueryKey = ["clubs"] as const;
export const clubListQueryKey = (page: number) => [...clubsQueryKey, "list", page] as const;
export const clubQueryKey = (id: string) => [...clubsQueryKey, "detail", id] as const;

// The generated types allow numbers as strings (JSON number formats); the API sends numbers.
function toClub(club: components["schemas"]["OwnerClubResponse"]): OwnerClub {
  return { ...club, cancellationWindowHours: Number(club.cancellationWindowHours) };
}

/** US06: the signed-in owner's clubs, one page at a time (by name). */
export async function fetchClubs(page: number): Promise<ClubPage> {
  const response = await apiJson<components["schemas"]["PagedResponseOfOwnerClubResponse"]>(
    `/api/manage/clubs?page=${page}&pageSize=${CLUBS_PAGE_SIZE}`,
  );
  return {
    items: response.items.map(toClub),
    page: Number(response.page),
    pageSize: Number(response.pageSize),
    totalCount: Number(response.totalCount),
  };
}

/** A club of the signed-in owner. Another owner's club and a missing one are both a 404. */
export async function fetchClub(id: string): Promise<OwnerClub> {
  return toClub(await apiJson<components["schemas"]["OwnerClubResponse"]>(`/api/manage/clubs/${id}`));
}

export async function createClub(request: ClubRequest): Promise<OwnerClub> {
  return toClub(await apiJson<components["schemas"]["OwnerClubResponse"]>("/api/manage/clubs", { method: "POST", body: JSON.stringify(request) }));
}

export async function updateClub(id: string, request: ClubRequest): Promise<OwnerClub> {
  return toClub(
    await apiJson<components["schemas"]["OwnerClubResponse"]>(`/api/manage/clubs/${id}`, { method: "PUT", body: JSON.stringify(request) }),
  );
}
