import type { Metadata } from "next";
import { PageShell } from "@/components/page-shell";
import { ClubForm } from "@/features/clubs/components/club-form";

export const metadata: Metadata = {
  title: "Novo clube",
};

export default function Page() {
  return (
    <PageShell title="Novo clube">
      <ClubForm />
    </PageShell>
  );
}
