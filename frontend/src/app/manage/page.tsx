import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { useTranslations } from "next-intl";
import { PageShell } from "@/components/page-shell";
import { ClubList } from "@/features/clubs/components/club-list";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages.manage");
  return {
    title: t("title"),
  };
}

export default function Page() {
  const t = useTranslations("pages.manage");
  return (
    <PageShell title={t("title")}>
      <ClubList />
    </PageShell>
  );
}
