import type { Metadata } from "next";
import { Suspense } from "react";
import { LoadingRegion } from "@/components/loading-region";
import { Skeleton } from "@/components/ui/skeleton";
import { PageShell } from "@/components/page-shell";
import { AdminDashboard } from "@/features/admin/components/admin-dashboard";
import { loadAdminStats } from "@/lib/server/admin-stats";

export const metadata: Metadata = {
  title: "Administração",
};

/** US24, US25, US26: the first load reads the totals on the server; the chart and the table load in the browser. */
export default function Page() {
  return (
    <PageShell title="Administração" width="wide">
      <Suspense
        fallback={
          <LoadingRegion label="A carregar…" className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {[0, 1, 2].map((key) => (
              <Skeleton key={key} className="h-24 w-full rounded-lg" />
            ))}
          </LoadingRegion>
        }
      >
        <Dashboard />
      </Suspense>
    </PageShell>
  );
}

/** Reads the request's cookies, so it renders inside the page's `<Suspense>` (Cache Components). */
async function Dashboard() {
  return <AdminDashboard initialStats={await loadAdminStats()} />;
}
