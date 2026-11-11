import { useTranslations } from "next-intl";
import { MAIN_ID } from "@/components/main";

/** UX-014: the first thing in the page for keyboard users, visible only with focus. */
export function SkipLink() {
  const t = useTranslations("skipLink");
  return (
    <a
      href={`#${MAIN_ID}`}
      className="sr-only rounded-md bg-background px-4 py-2 text-sm font-medium shadow-md focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-50"
    >
      {t("label")}
    </a>
  );
}
