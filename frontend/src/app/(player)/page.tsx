import type { Metadata } from "next";
import { Suspense } from "react";
import { PageShell } from "@/components/page-shell";
import { RouteLoading } from "@/components/route-loading";
import { ClubSearch } from "@/features/catalog/components/club-search";

export const metadata: Metadata = {
  title: "Pesquisar clubes",
};

export default function Page() {
  return (
    <PageShell title="Encontra onde jogar">
      {/* The search reads the URL in the browser (`useSearchParams`), which Cache Components wants inside a boundary. */}
      <Suspense fallback={<RouteLoading />}>
        <ClubSearch />
      </Suspense>
    </PageShell>
  );
}
