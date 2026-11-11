import Link from "next/link";
import { useTranslations } from "next-intl";
import { LanguageSwitcher } from "@/components/language-switcher";
import { PAGE_WIDTH } from "@/components/page-width";
import { PRIVACY_PATH, TERMS_PATH } from "@/features/legal/legal-info";

const LINK_CLASS = "inline-block py-1 underline underline-offset-4";

/** T07: the links to the legal pages, in the root layout so that every page has them; T09: the language switcher again. */
export function SiteFooter() {
  const t = useTranslations("footer");
  return (
    <footer className="mt-auto border-t">
      <div className={`mx-auto flex w-full ${PAGE_WIDTH.narrow} flex-wrap items-center justify-between gap-x-6 gap-y-1 px-4 py-3 text-sm text-muted-foreground`}>
        <nav aria-label={t("legalLabel")} className="flex flex-wrap gap-x-6 gap-y-1">
          <Link href={TERMS_PATH} className={LINK_CLASS}>
            {t("terms")}
          </Link>
          <Link href={PRIVACY_PATH} className={LINK_CLASS}>
            {t("privacy")}
          </Link>
        </nav>
        <LanguageSwitcher />
      </div>
    </footer>
  );
}
