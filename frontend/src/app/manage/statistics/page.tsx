import type { Metadata } from "next";
import { Suspense } from "react";
import { PageShell } from "@/components/page-shell";
import { RouteLoading } from "@/components/route-loading";
import { OwnerStatistics } from "@/features/statistics/components/owner-statistics";

export const metadata: Metadata = {
  title: "Estatísticas · Reservas de Campos",
};

export default function Page() {
  return (
    <PageShell title="Estatísticas">
      {/* The period is read from the URL in the browser (`useSearchParams`), which Cache Components wants inside a boundary. */}
      <Suspense fallback={<RouteLoading />}>
        <OwnerStatistics />
      </Suspense>
    </PageShell>
  );
}
