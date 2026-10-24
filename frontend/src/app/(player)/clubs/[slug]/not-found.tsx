import { PageShell } from "@/components/page-shell";
import { PublicClubNotFound } from "@/features/catalog/components/public-club-not-found";

export default function NotFound() {
  return (
    <PageShell title="Clube">
      <PublicClubNotFound />
    </PageShell>
  );
}
