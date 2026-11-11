import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { PageShell } from "@/components/page-shell";
import { EditClub } from "@/features/clubs/components/edit-club";
import { isGuid } from "@/lib/guid";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages.editClub");
  return {
    title: t("title"),
  };
}

export default async function Page({ params }: PageProps<"/manage/clubs/[id]">) {
  const { id } = await params;
  // Public ids are GUIDs: anything else is a club that cannot exist, with no need to ask the API.
  if (!isGuid(id)) {
    notFound();
  }
  const t = await getTranslations("pages.editClub");

  return (
    <PageShell title={t("title")}>
      <EditClub clubId={id} />
    </PageShell>
  );
}
