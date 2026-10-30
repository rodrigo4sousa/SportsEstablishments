import type { Metadata } from "next";
import { PageShell } from "@/components/page-shell";
import { ClubList } from "@/features/clubs/components/club-list";

export const metadata: Metadata = {
  title: "Os teus clubes",
};

export default function Page() {
  return (
    <PageShell title="Os teus clubes">
      <ClubList />
    </PageShell>
  );
}
