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

// ---- US08: slot length, weekly hours and the slot preview

export type DayHours = { closed: boolean; opens: string | null; closes: string | null };

/** The same shape goes to the API and comes back from it. */
export type PitchSchedule = {
  slotDurationMinutes: number;
  monday: DayHours;
  tuesday: DayHours;
  wednesday: DayHours;
  thursday: DayHours;
  friday: DayHours;
  saturday: DayHours;
  sunday: DayHours;
};

export type PreviewSlot = { startsAt: string; endsAt: string };

export type PitchSlots = { date: string; slotDurationMinutes: number; slots: PreviewSlot[] };

/** A pitch's schedule and slot preview sit under the pitch's own key inside the club's. */
export const pitchKey = (clubId: string, pitchId: string) => [...pitchesQueryKey(clubId), pitchId] as const;
export const scheduleQueryKey = (clubId: string, pitchId: string) => [...pitchKey(clubId, pitchId), "schedule"] as const;
export const slotsQueryKey = (clubId: string, pitchId: string, date?: string) =>
  date === undefined ? ([...pitchKey(clubId, pitchId), "slots"] as const) : ([...pitchKey(clubId, pitchId), "slots", date] as const);

const schedulePath = (clubId: string, pitchId: string) => `${pitchesPath(clubId)}/${pitchId}/schedule`;

function toSchedule(schedule: components["schemas"]["PitchScheduleResponse"]): PitchSchedule {
  return { ...schedule, slotDurationMinutes: Number(schedule.slotDurationMinutes) };
}

export async function fetchSchedule(clubId: string, pitchId: string): Promise<PitchSchedule> {
  return toSchedule(await apiJson<components["schemas"]["PitchScheduleResponse"]>(schedulePath(clubId, pitchId)));
}

/** Replaces the slot length and the whole week. */
export async function saveSchedule(clubId: string, pitchId: string, schedule: PitchSchedule): Promise<PitchSchedule> {
  return toSchedule(
    await apiJson<components["schemas"]["PitchScheduleResponse"]>(schedulePath(clubId, pitchId), { method: "PUT", body: JSON.stringify(schedule) }),
  );
}

/** The slots the schedule generates on a local date ("YYYY-MM-DD"), as UTC instants. Bookings are not taken out. */
export async function fetchSlots(clubId: string, pitchId: string, date: string): Promise<PitchSlots> {
  const response = await apiJson<components["schemas"]["PitchSlotsResponse"]>(
    `${pitchesPath(clubId)}/${pitchId}/slots?date=${encodeURIComponent(date)}`,
  );
  return { ...response, slotDurationMinutes: Number(response.slotDurationMinutes) };
}
