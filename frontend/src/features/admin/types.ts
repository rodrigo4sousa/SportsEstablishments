import type { components } from "@/lib/api/schema";

type Schemas = components["schemas"];

export type Granularity = "day" | "week" | "month";

export type AccountCounts = { active: number; deactivated: number };

/** US24: the platform's totals; numbers only. */
export type AdminStats = {
  owners: AccountCounts;
  clubs: { total: number; visible: number; ofDeactivatedOwners: number; withoutActivePitches: number };
  activePitches: number;
  players: AccountCounts;
  confirmedBookings: number;
};

export type PeriodBucket = { start: string; end: string; count: number };

/** US25: `from` and `to` are the whole periods the asked range was widened to. */
export type BookingsOverTime = {
  granularity: Granularity;
  from: string;
  to: string;
  includeCancelled: boolean;
  total: number;
  buckets: PeriodBucket[];
};

export type OwnerSort = "name" | "email" | "registeredAt" | "status" | "clubs" | "pitches" | "bookings" | "players";
export type SortDirection = "asc" | "desc";

/** US26: one owner with counts; the only personal data on the admin pages is the owner's own name and email. */
export type AdminOwner = {
  id: string;
  name: string;
  email: string;
  registeredAt: string;
  deactivated: boolean;
  clubs: number;
  pitches: number;
  bookings: number;
  players: number;
};

/** US29: one owner, with how many confirmed bookings in their clubs have not ended yet (a deactivation cancels none of them). */
export type AdminOwnerDetail = AdminOwner & { futureBookings: number };

export type OwnerStatusAction = "deactivated" | "reactivated";

/** US29: one entry of an owner's history: what happened, why, which admin did it and when (an instant). */
export type OwnerStatusChange = {
  id: string;
  action: OwnerStatusAction;
  reason: string;
  adminId: string;
  adminName: string;
  at: string;
};

export type AdminOwnersPage = { items: AdminOwner[]; page: number; pageSize: number; totalCount: number };

const counts = (value: Schemas["AccountCounts"]): AccountCounts => ({ active: Number(value.active), deactivated: Number(value.deactivated) });

export function toAdminStats(response: Schemas["AdminStatsResponse"]): AdminStats {
  return {
    owners: counts(response.owners),
    clubs: {
      total: Number(response.clubs.total),
      visible: Number(response.clubs.visible),
      ofDeactivatedOwners: Number(response.clubs.ofDeactivatedOwners),
      withoutActivePitches: Number(response.clubs.withoutActivePitches),
    },
    activePitches: Number(response.activePitches),
    players: counts(response.players),
    confirmedBookings: Number(response.confirmedBookings),
  };
}

export function toBookingsOverTime(response: Schemas["BookingsOverTimeResponse"]): BookingsOverTime {
  return {
    granularity: response.granularity as Granularity,
    from: response.from,
    to: response.to,
    includeCancelled: response.includeCancelled,
    total: Number(response.total),
    buckets: response.buckets.map((bucket) => ({ start: bucket.start, end: bucket.end, count: Number(bucket.count) })),
  };
}

export function toAdminOwnerDetail(response: Schemas["AdminOwnerDetailResponse"]): AdminOwnerDetail {
  return {
    id: response.id,
    name: response.name,
    email: response.email,
    registeredAt: response.registeredAt,
    deactivated: response.deactivated,
    clubs: Number(response.clubs),
    pitches: Number(response.pitches),
    bookings: Number(response.bookings),
    players: Number(response.players),
    futureBookings: Number(response.futureBookings),
  };
}

export function toOwnerStatusChange(response: Schemas["OwnerStatusChangeResponse"]): OwnerStatusChange {
  return {
    id: response.id,
    action: response.action === "reactivated" ? "reactivated" : "deactivated",
    reason: response.reason,
    adminId: response.adminId,
    adminName: response.adminName,
    at: response.at,
  };
}

export function toAdminOwnersPage(response: Schemas["PagedResponseOfAdminOwnerResponse"]): AdminOwnersPage {
  return {
    items: response.items.map((owner) => ({
      id: owner.id,
      name: owner.name,
      email: owner.email,
      registeredAt: owner.registeredAt,
      deactivated: owner.deactivated,
      clubs: Number(owner.clubs),
      pitches: Number(owner.pitches),
      bookings: Number(owner.bookings),
      players: Number(owner.players),
    })),
    page: Number(response.page),
    pageSize: Number(response.pageSize),
    totalCount: Number(response.totalCount),
  };
}
