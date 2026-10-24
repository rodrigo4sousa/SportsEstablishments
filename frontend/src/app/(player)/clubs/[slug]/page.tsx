import type { Metadata } from "next";
import Link from "next/link";
import { PageShell } from "@/components/page-shell";
import { ClubDetails } from "@/features/catalog/components/club-details";
import { loadPublicClub } from "@/lib/server/public-club";

export const metadata: Metadata = {
  title: "Clube · Reservas de Campos",
};

export default async function Page({ params }: PageProps<"/clubs/[slug]">) {
  const { slug } = await params;
  const club = await loadPublicClub(slug);

  return (
    <PageShell title={club.name}>
      <p>
        <Link href="/" className="text-sm text-muted-foreground underline underline-offset-4">
          ← Pesquisar clubes
        </Link>
      </p>
      <ClubDetails club={club} />
    </PageShell>
  );
}
