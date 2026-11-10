"use client";

import Link from "next/link";
import { Button } from "@/components/ui/button";
import { AccountMenu, type AccountMenuLink } from "@/components/account-menu";
import { useCurrentUser } from "../hooks/use-current-user";

const ACCOUNT_LINK = { href: "/account", label: "A minha conta" };

/** The admin has no link: the account page only offers it the export, and its area is `/admin`. */
const LINKS: Record<string, AccountMenuLink[]> = {
  Player: [{ href: "/bookings", label: "As minhas reservas" }, ACCOUNT_LINK],
  Owner: [ACCOUNT_LINK],
};

/**
 * Header corner: who is signed in, or the way to create an account.
 *
 * Signed in, it is the shared `AccountMenu` (the player's bookings, «Olá, …», «Sair»; a «Conta» disclosure on a phone, UX-021).
 */
export function AccountStatus() {
  const { data: account, isPending } = useCurrentUser();
  if (isPending) {
    return null;
  }

  if (account) {
    return <AccountMenu accountName={account.name} links={LINKS[account.profile] ?? []} />;
  }

  return (
    <nav aria-label="Conta" className="flex items-center gap-2">
      <Button asChild variant="ghost" size="sm">
        <Link href="/login">Entrar</Link>
      </Button>
      <Button asChild variant="outline" size="sm">
        <Link href="/register">Criar conta</Link>
      </Button>
    </nav>
  );
}
