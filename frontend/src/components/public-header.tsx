import Link from "next/link";
import { useTranslations } from "next-intl";
import { LanguageSwitcher } from "@/components/language-switcher";
import { PAGE_WIDTH } from "@/components/page-width";
import { AccountStatus } from "@/features/auth/components/account-status";

type PublicHeaderProps = {
  /** Show who is signed in, or the sign-in and sign-up links. The account pages leave it out: they are those links' destination. */
  account?: boolean;
};

/** The header of the player's pages, the account pages and the error and not-found pages: the brand, the language and the account links. */
export function PublicHeader({ account = true }: PublicHeaderProps) {
  const t = useTranslations("header");
  return (
    <header className="border-b">
      <div className={`mx-auto flex w-full ${PAGE_WIDTH.narrow} flex-wrap items-center justify-between gap-x-4 gap-y-2 px-4 py-3`}>
        <Link href="/" className="font-semibold tracking-tight whitespace-nowrap">
          {t("brand")}
        </Link>
        <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
          <LanguageSwitcher />
          {account ? <AccountStatus /> : null}
        </div>
      </div>
    </header>
  );
}
