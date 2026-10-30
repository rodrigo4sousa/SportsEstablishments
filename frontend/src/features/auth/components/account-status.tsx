"use client";

import { ChevronDownIcon } from "lucide-react";
import Link from "next/link";
import { useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { LogoutButton } from "./logout-button";
import { useCurrentUser } from "../hooks/use-current-user";

const MENU_ID = "account-menu";

/**
 * Header corner: who is signed in, or the way to create an account.
 *
 * Signed in, from `sm` up it is a row (the player's bookings, «Olá, …», «Sair»). On a phone that row does not fit next to the brand
 * without wrapping onto two lines (UX-021), so it is a menu button, «Conta», that opens the same items in a panel: a plain disclosure
 * (a button with `aria-expanded` that shows a list of links and buttons), not an ARIA menu, so Tab walks it like any other part of the
 * page. Escape closes it and returns focus to the button, and so does leaving it with Tab or a click elsewhere. It is one set of
 * elements, shown by CSS, so a screen reader never meets them twice.
 */
export function AccountStatus() {
  const { data: account, isPending } = useCurrentUser();
  const [open, setOpen] = useState(false);
  const toggle = useRef<HTMLButtonElement>(null);

  if (isPending) {
    return null;
  }

  if (account) {
    return (
      <nav
        aria-label="Conta"
        className="relative"
        onBlur={(event) => {
          if (!event.currentTarget.contains(event.relatedTarget)) {
            setOpen(false);
          }
        }}
        onKeyDown={(event) => {
          if (event.key === "Escape" && open) {
            setOpen(false);
            toggle.current?.focus();
          }
        }}
      >
        <Button
          ref={toggle}
          type="button"
          variant="outline"
          size="cta"
          aria-expanded={open}
          aria-controls={MENU_ID}
          className="sm:hidden"
          onClick={() => setOpen((current) => !current)}
        >
          Conta
          <ChevronDownIcon aria-hidden="true" className={open ? "rotate-180" : ""} />
        </Button>
        <div
          id={MENU_ID}
          className={`${open ? "flex" : "hidden"} absolute top-full right-0 z-40 mt-2 w-64 flex-col items-stretch gap-1 rounded-md border bg-background p-2 shadow-md sm:static sm:mt-0 sm:flex sm:w-auto sm:flex-row sm:items-center sm:gap-3 sm:border-0 sm:p-0 sm:shadow-none`}
        >
          <p className="px-2 py-1 text-sm sm:p-0">
            Olá, <span className="font-medium">{account.name}</span>
          </p>
          {account.profile === "Player" ? (
            <Link
              href="/bookings"
              onClick={() => setOpen(false)}
              className="flex min-h-11 items-center rounded-md px-2 text-sm underline-offset-4 hover:bg-accent sm:order-first sm:min-h-0 sm:p-0 sm:hover:bg-transparent sm:hover:underline"
            >
              As minhas reservas
            </Link>
          ) : null}
          <LogoutButton size="cta" />
        </div>
      </nav>
    );
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
