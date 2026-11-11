import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { useTranslations } from "next-intl";
import { PageShell } from "@/components/page-shell";
import { ClubForm } from "@/features/clubs/components/club-form";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages.newClub");
  return {
    title: t("title"),
  };
}

export default function Page() {
  const t = useTranslations("pages.newClub");
  return (
    <PageShell title={t("title")}>
      <ClubForm />
    </PageShell>
  );
}
