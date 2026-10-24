import { apiJson } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";

export type PitchStatus = "active" | "inactive";

export type Pitch = Omit<components["schemas"]["PitchResponse"], "pricePerSlot" | "status"> & {
  pricePerSlot: number;
  status: PitchStatus;
};

/** What the API takes; the price goes as a number of euros. */
export type PitchRequest = { name: string; sport: string; pricePerSlot: number };

export type PitchPage = {
  items: Pitch[];
  page: number;
  pageSize: number;
  totalCount: number;
};

export const PITCHES_PAGE_SIZE = 20;

/** Everything about one club's pitches sits under this key, so one invalidation refreshes the list. */
export const pitchesQueryKey = (clubId: string) => ["pitches", clubId] as const;
export const pitchListQueryKey = (clubId: string, page: number) => [...pitchesQueryKey(clubId), "list", page] as const;

const pitchesPath = (clubId: string) => `/api/manage/clubs/${clubId}/pitches`;

// The generated types allow numbers as strings (JSON number formats); the API sends numbers.
function toPitch(pitch: components["schemas"]["PitchResponse"]): Pitch {
  return { ...pitch, pricePerSlot: Number(pitch.pricePerSlot), status: pitch.status === "inactive" ? "inactive" : "active" };
}

/** US07: the club's pitches, active and inactive, by name, one page at a time. */
export async function fetchPitches(clubId: string, page: number): Promise<PitchPage> {
  const response = await apiJson<components["schemas"]["PagedResponseOfPitchResponse"]>(
    `${pitchesPath(clubId)}?page=${page}&pageSize=${PITCHES_PAGE_SIZE}`,
  );
  return {
    items: response.items.map(toPitch),
    page: Number(response.page),
    pageSize: Number(response.pageSize),
    totalCount: Number(response.totalCount),
  };
}

export async function createPitch(clubId: string, request: PitchRequest): Promise<Pitch> {
  return toPitch(await apiJson<components["schemas"]["PitchResponse"]>(pitchesPath(clubId), { method: "POST", body: JSON.stringify(request) }));
}

export async function updatePitch(clubId: string, pitchId: string, request: PitchRequest): Promise<Pitch> {
  return toPitch(
    await apiJson<components["schemas"]["PitchResponse"]>(`${pitchesPath(clubId)}/${pitchId}`, { method: "PUT", body: JSON.stringify(request) }),
  );
}

export async function deactivatePitch(clubId: string, pitchId: string): Promise<Pitch> {
  return toPitch(await apiJson<components["schemas"]["PitchResponse"]>(`${pitchesPath(clubId)}/${pitchId}/deactivate`, { method: "POST" }));
}

export async function reactivatePitch(clubId: string, pitchId: string): Promise<Pitch> {
  return toPitch(await apiJson<components["schemas"]["PitchResponse"]>(`${pitchesPath(clubId)}/${pitchId}/reactivate`, { method: "POST" }));
}
