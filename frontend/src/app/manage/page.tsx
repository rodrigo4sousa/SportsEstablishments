import { PageShell } from "@/components/page-shell";
import { ClubList } from "@/features/clubs/components/club-list";

export default function Page() {
  return (
    <PageShell title="Os teus clubes">
      <ClubList />
    </PageShell>
  );
}
