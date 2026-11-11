import Link from "next/link";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";

/**
 * Shown for a club that does not exist and for a club of another owner alike (T02: the API answers the same 404 for
 * both, and the page must not tell them apart either).
 */
export function ClubNotFound() {
  const t = useTranslations("clubs.notFound");
  return (
    <section className="flex flex-col items-start gap-4">
      <h2 className="text-2xl font-semibold tracking-tight">{t("title")}</h2>
      <p className="max-w-prose text-muted-foreground">{t("body")}</p>
      <Button asChild variant="outline">
        <Link href="/manage">{t("back")}</Link>
      </Button>
    </section>
  );
}
