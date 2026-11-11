import Link from "next/link";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";

/** The owner's home while they have no club (US02, US06): invites them to create the first one. */
export function NoClubsYet() {
  const t = useTranslations("clubs.none");
  return (
    <section className="flex flex-col gap-4">
      <h2 className="text-2xl font-semibold tracking-tight">{t("title")}</h2>
      <p className="max-w-prose text-muted-foreground">{t("body")}</p>
      <div>
        <Button asChild>
          <Link href="/manage/clubs/new">{t("create")}</Link>
        </Button>
      </div>
    </section>
  );
}
