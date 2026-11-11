import { useTranslations } from "next-intl";
import { PageShell } from "@/components/page-shell";
import { PublicClubNotFound } from "@/features/catalog/components/public-club-not-found";

export default function NotFound() {
  const t = useTranslations("pages.club");
  return (
    <PageShell title={t("notFoundHeading")}>
      <PublicClubNotFound />
    </PageShell>
  );
}
