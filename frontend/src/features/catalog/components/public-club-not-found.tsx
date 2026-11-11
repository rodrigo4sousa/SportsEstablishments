import Link from "next/link";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";

/**
 * Shown for a club that does not exist and for one that is hidden (no active pitch, or its owner is deactivated):
 * the API answers the same 404, and the page does not tell them apart either.
 */
export function PublicClubNotFound() {
  const t = useTranslations("catalog.notFound");
  return (
    <section className="flex flex-col items-start gap-4">
      <h2 className="text-2xl font-semibold tracking-tight">{t("title")}</h2>
      <p className="max-w-prose text-muted-foreground">{t("body")}</p>
      <Button asChild variant="outline">
        <Link href="/">{t("search")}</Link>
      </Button>
    </section>
  );
}
