import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { PageShell } from "@/components/page-shell";
import { EditClub } from "@/features/clubs/components/edit-club";
import { isGuid } from "@/lib/guid";

export const metadata: Metadata = {
  title: "Editar clube",
};

export default async function Page({ params }: PageProps<"/manage/clubs/[id]">) {
  const { id } = await params;
  // Public ids are GUIDs: anything else is a club that cannot exist, with no need to ask the API.
  if (!isGuid(id)) {
    notFound();
  }

  return (
    <PageShell title="Editar clube">
      <EditClub clubId={id} />
    </PageShell>
  );
}
