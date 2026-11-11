import { useTranslations } from "next-intl";
import { PageShell } from "@/components/page-shell";
import { ClubNotFound } from "@/features/clubs/components/club-not-found";

export default function NotFound() {
  const t = useTranslations("pages.calendar");
  return (
    <PageShell title={t("title")}>
      <ClubNotFound />
    </PageShell>
  );
}
