"use client";

import { useQueryClient } from "@tanstack/react-query";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect } from "react";
import { ApiError } from "@/lib/api/client";
import { loginUrl } from "@/lib/auth/redirects";
import { adminQueryKey, type ChartQuery, type OwnersQuery } from "../api";
import { PeriodFilter } from "@/components/period-filter";
import { defaultRange } from "@/features/statistics/components/owner-statistics";
import { adminHref, chartFromParams, ownersFromParams, registrationsFromParams } from "../admin-url";
import type { AdminStats, RegistrationsGranularity } from "../types";
import { BookingsChart } from "./bookings-chart";
import { OwnersTable } from "./owners-table";
import { RegistrationsChart } from "./registrations-chart";
import { StatsCards } from "./stats-cards";
import { TopOccupationTable } from "./top-occupation-table";

type AdminDashboardProps = {
  initialStats: AdminStats | null;
};

/**
 * `/admin` (US24 to US28): the totals; one period (`from`, `to`, the last 30 days without them) that the bookings chart, the
 * registrations chart and the top of the clubs all use; and the owners table. The filters, the table's search, sort and page and
 * the registrations' grouping live in the URL, so a reload or a shared link shows the same view and the back button steps through
 * the changes. A session that ended and cannot be renewed sends the admin back to sign in.
 */
export function AdminDashboard({ initialStats }: AdminDashboardProps) {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const queryClient = useQueryClient();
  const chart = chartFromParams(params);
  const owners = ownersFromParams(params);
  const registrations = registrationsFromParams(params);
  const fallback = defaultRange();

  const here = adminHref(pathname, chart, owners, registrations);
  useEffect(() => {
    // Any request of the page that comes back 401 (after the shared refresh) means the session is over.
    return queryClient.getQueryCache().subscribe((event) => {
      const error = event.query.state.error;
      if (event.query.queryKey[0] === adminQueryKey[0] && error instanceof ApiError && error.status === 401) {
        router.replace(loginUrl(here));
      }
    });
  }, [queryClient, router, here]);

  const changeChart = (patch: Partial<ChartQuery>) => router.replace(adminHref(pathname, { ...chart, ...patch }, owners, registrations), { scroll: false });
  const changeOwners = (patch: Partial<OwnersQuery>) => router.replace(adminHref(pathname, chart, { ...owners, ...patch }, registrations), { scroll: false });
  const changeRegistrations = (next: RegistrationsGranularity) => router.replace(adminHref(pathname, chart, owners, next), { scroll: false });
  const period = { from: chart.from, to: chart.to };

  return (
    <div className="flex flex-col gap-10">
      <section aria-labelledby="admin-totals" className="flex flex-col gap-4">
        <h2 id="admin-totals" className="text-lg font-semibold">
          Totais
        </h2>
        <StatsCards initialStats={initialStats} />
      </section>
      <section aria-labelledby="admin-period" className="flex flex-col gap-3">
        <h2 id="admin-period" className="text-lg font-semibold">
          Período
        </h2>
        <p className="text-sm text-muted-foreground">Vale para as reservas, os registos e os clubes com maior ocupação.</p>
        <PeriodFilter
          idPrefix="admin-period"
          from={chart.from}
          to={chart.to}
          defaultFrom={fallback.from}
          defaultTo={fallback.to}
          onChange={(next) => changeChart(next)}
        />
      </section>
      <section aria-labelledby="admin-chart" className="flex flex-col gap-4">
        <h2 id="admin-chart" className="text-lg font-semibold">
          Reservas ao longo do tempo
        </h2>
        <BookingsChart query={chart} onChange={changeChart} />
      </section>
      <section aria-labelledby="admin-registrations" className="flex flex-col gap-4">
        <h2 id="admin-registrations" className="text-lg font-semibold">
          Novos registos
        </h2>
        <RegistrationsChart period={period} granularity={registrations} onGranularity={changeRegistrations} />
      </section>
      <section aria-labelledby="admin-top" className="flex flex-col gap-4">
        <h2 id="admin-top" className="text-lg font-semibold">
          Clubes com maior ocupação
        </h2>
        <TopOccupationTable period={period} />
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
