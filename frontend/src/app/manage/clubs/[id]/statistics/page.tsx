import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { PageShell } from "@/components/page-shell";
import { RouteLoading } from "@/components/route-loading";
import { ClubStatistics } from "@/features/statistics/components/club-statistics";
import { isGuid } from "@/lib/guid";

export const metadata: Metadata = {
  title: "Estatísticas do clube · Reservas de Campos",
};

export default async function Page({ params }: PageProps<"/manage/clubs/[id]/statistics">) {
  const { id } = await params;
  // Public ids are GUIDs: anything else is a club that cannot exist, with no need to ask the API.
  if (!isGuid(id)) {
    notFound();
  }

  return (
    <PageShell title="Estatísticas do clube">
      <Suspense fallback={<RouteLoading />}>
        <ClubStatistics clubId={id} />
      </Suspense>
    </PageShell>
  );
}
