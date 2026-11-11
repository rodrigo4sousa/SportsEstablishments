import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { useTranslations } from "next-intl";
import { PageShell } from "@/components/page-shell";
import { PrivacySection } from "@/features/account/components/privacy-section";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages.account");
  return {
    title: t("title"),
    robots: { index: false },
  };
}

export default function Page() {
  const t = useTranslations("pages.account");
  return (
    <PageShell title={t("title")}>
      <PrivacySection />
    </PageShell>
  );
}
