import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";
import { PageShell } from "@/components/page-shell";
import { RouteLoading } from "@/components/route-loading";
import { ClubSearch } from "@/features/catalog/components/club-search";

export const metadata: Metadata = {
  title: "Pesquisar clubes",
};

export default function Page() {
  return (
    <PageShell
      title="Encontra onde jogar"
      description={
        <div className="flex flex-col gap-2 text-muted-foreground">
          <p>Pesquisa clubes de futsal, futebol 7, padel e ténis, vê os horários livres e reserva o teu campo.</p>
          <p>
            Podes pesquisar sem conta; para reservar precisas de uma conta de jogador. Tens um clube?{" "}
            <Link href="/register/owner" className="text-foreground underline underline-offset-4">
              Cria uma conta de dono
            </Link>
            .
          </p>
        </div>
      }
    >
      {/* The search reads the URL in the browser (`useSearchParams`), which Cache Components wants inside a boundary. */}
      <Suspense fallback={<RouteLoading />}>
        <ClubSearch />
      </Suspense>
    </PageShell>
  );
}
