import Link from "next/link";
import { useTranslations } from "next-intl";
import { Main } from "@/components/main";
import { PAGE_WIDTH } from "@/components/page-width";
import { PublicHeader } from "@/components/public-header";
import { Button } from "@/components/ui/button";

/** UX-007: the page for an address that does not exist, in the site's language and with the way back, instead of Next.js's default. */
export default function NotFound() {
  const t = useTranslations("notFound");
  return (
    <>
      {/* Not a `metadata` export: not-found pages do not take one, and React puts a `<title>` in the head. */}
      <title>{t("documentTitle")}</title>
      <PublicHeader />
      <Main className={`mx-auto flex w-full ${PAGE_WIDTH.narrow} flex-1 flex-col items-start gap-4 px-4 py-12`}>
        <h1 className="text-2xl font-semibold tracking-tight">{t("title")}</h1>
        <p className="text-muted-foreground">{t("body")}</p>
        <div className="flex flex-wrap items-center gap-3">
          <Button asChild>
            <Link href="/">{t("searchClubs")}</Link>
          </Button>
        </div>
      </Main>
    </>
  );
}
