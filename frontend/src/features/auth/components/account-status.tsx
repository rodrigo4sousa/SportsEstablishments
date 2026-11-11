"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { AccountMenu, type AccountMenuLink } from "@/components/account-menu";
import { useCurrentUser } from "../hooks/use-current-user";

const ACCOUNT_LINK = { href: "/account", label: "myAccount" } as const;

/** The admin has no link: the account page only offers it the export, and its area is `/admin`. */
const LINKS: Record<string, ReadonlyArray<{ href: string; label: "myBookings" | "myAccount" }>> = {
  Player: [{ href: "/bookings", label: "myBookings" }, ACCOUNT_LINK],
  Owner: [ACCOUNT_LINK],
};

/**
 * Header corner: who is signed in, or the way to create an account.
 *
 * Signed in, it is the shared `AccountMenu` (the player's bookings, «Olá, …», «Sair»; a «Conta» disclosure on a phone, UX-021).
 */
export function AccountStatus() {
  const t = useTranslations("auth.accountStatus");
  const { data: account, isPending } = useCurrentUser();
  if (isPending) {
    return null;
  }

  if (account) {
    const links: AccountMenuLink[] = (LINKS[account.profile] ?? []).map((link) => ({ href: link.href, label: t(link.label) }));
    return <AccountMenu accountName={account.name} links={links} />;
  }

  return (
    <nav aria-label={t("label")} className="flex items-center gap-2">
      <Button asChild variant="ghost" size="sm">
        <Link href="/login">{t("signIn")}</Link>
      </Button>
      <Button asChild variant="outline" size="sm">
        <Link href="/register">{t("register")}</Link>
      </Button>
    </nav>
  );
}
