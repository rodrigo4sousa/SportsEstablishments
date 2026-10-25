"use client";

import { useQueryClient } from "@tanstack/react-query";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect } from "react";
import { ApiError } from "@/lib/api/client";
import { loginUrl } from "@/lib/auth/redirects";
import { adminQueryKey, type ChartQuery, type OwnersQuery } from "../api";
import { adminHref, chartFromParams, ownersFromParams } from "../admin-url";
import type { AdminStats } from "../types";
import { BookingsChart } from "./bookings-chart";
import { OwnersTable } from "./owners-table";
import { StatsCards } from "./stats-cards";

type AdminDashboardProps = {
  initialStats: AdminStats | null;
};

/**
 * `/admin` (US24, US25, US26): the totals, the bookings chart and the owners table. The chart's filters and the
 * table's search, sort and page live in the URL, so a reload or a shared link shows the same view and the back
 * button steps through the changes. A session that ended and cannot be renewed sends the admin back to sign in.
 */
export function AdminDashboard({ initialStats }: AdminDashboardProps) {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const queryClient = useQueryClient();
  const chart = chartFromParams(params);
  const owners = ownersFromParams(params);

  const here = adminHref(pathname, chart, owners);
  useEffect(() => {
    // Any request of the page that comes back 401 (after the shared refresh) means the session is over.
    return queryClient.getQueryCache().subscribe((event) => {
      const error = event.query.state.error;
      if (event.query.queryKey[0] === adminQueryKey[0] && error instanceof ApiError && error.status === 401) {
        router.replace(loginUrl(here));
      }
    });
  }, [queryClient, router, here]);

  const changeChart = (patch: Partial<ChartQuery>) => router.replace(adminHref(pathname, { ...chart, ...patch }, owners), { scroll: false });
  const changeOwners = (patch: Partial<OwnersQuery>) => router.replace(adminHref(pathname, chart, { ...owners, ...patch }), { scroll: false });

  return (
    <div className="flex flex-col gap-10">
      <section aria-labelledby="admin-totals" className="flex flex-col gap-4">
        <h2 id="admin-totals" className="text-lg font-semibold">
          Totais
        </h2>
        <StatsCards initialStats={initialStats} />
      </section>
      <section aria-labelledby="admin-chart" className="flex flex-col gap-4">
        <h2 id="admin-chart" className="text-lg font-semibold">
          Reservas ao longo do tempo
        </h2>
        <BookingsChart query={chart} onChange={changeChart} />
      </section>
      <section aria-labelledby="admin-owners" className="flex flex-col gap-4">
        <h2 id="admin-owners" className="text-lg font-semibold">
          Donos
        </h2>
        <OwnersTable query={owners} onChange={changeOwners} />
      </section>
    </div>
  );
}
