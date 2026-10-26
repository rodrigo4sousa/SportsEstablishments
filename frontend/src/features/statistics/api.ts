import { apiJson } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";
import { toClubStatistics, toOwnerStatistics, type ClubStatistics, type OwnerStatistics } from "./types";

/** Everything the owner's statistics read sits under this key. */
export const statisticsQueryKey = ["statistics"] as const;

/** The period as typed: Lisbon dates `YYYY-MM-DD`, or null for the API's default (the last 30 days). */
export type StatisticsRange = { from: string | null; to: string | null };

export const ownerStatisticsQueryKey = (range: StatisticsRange) => [...statisticsQueryKey, "owner", range.from, range.to] as const;
export const clubStatisticsQueryKey = (clubId: string, range: StatisticsRange, pitchId: string | null) =>
  [...statisticsQueryKey, "club", clubId, range.from, range.to, pitchId] as const;

function query(range: StatisticsRange, pitchId?: string | null): string {
  const params = new URLSearchParams();
  if (range.from) params.set("from", range.from);
  if (range.to) params.set("to", range.to);
  if (pitchId) params.set("pitchId", pitchId);
  const text = params.toString();
  return text ? `?${text}` : "";
}

/** US14: every club of the signed-in owner over the period, one line each and the total. */
export async function fetchOwnerStatistics(range: StatisticsRange): Promise<OwnerStatistics> {
  return toOwnerStatistics(await apiJson<components["schemas"]["OwnerStatisticsResponse"]>(`/api/manage/statistics${query(range)}`));
}

/** US14: one club over the period, per pitch and in total; `pitchId` keeps one pitch. 404 `club_not_found` / `pitch_not_found`. */
export async function fetchClubStatistics(clubId: string, range: StatisticsRange, pitchId: string | null): Promise<ClubStatistics> {
  return toClubStatistics(
    await apiJson<components["schemas"]["ClubStatisticsResponse"]>(`/api/manage/clubs/${encodeURIComponent(clubId)}/statistics${query(range, pitchId)}`),
  );
}
