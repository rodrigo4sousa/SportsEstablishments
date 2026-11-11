"use client";

import { ChevronDownIcon } from "lucide-react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { LogoutButton } from "@/features/auth/components/logout-button";

/** There is one account menu per page, so its id is fixed. */
const MENU_ID = "account-menu";

export type AccountMenuLink = { href: string; label: string };

type AccountMenuProps = {
  accountName: string;
  /** The areas the signed-in person can go to; shown before «Olá, …» from `sm` up. */
  links?: AccountMenuLink[];
};

/**
 * The signed-in corner of every header (player, owner, admin): the person's areas, «Olá, …» and «Sair».
 *
 * From `sm` up it is a row. On a phone that row does not fit next to the brand without wrapping (UX-021), so it is a menu button,
 * «Conta», that opens the same items in a panel: a plain disclosure (a button with `aria-expanded` that shows links and a button),
 * not an ARIA menu, so Tab walks it like any other part of the page. Escape closes it and returns focus to the button, and so does
 * leaving it with Tab or a click elsewhere. It is one set of elements, shown by CSS, so a screen reader never meets them twice.
 */
export function AccountMenu({ accountName, links = [] }: AccountMenuProps) {
  const t = useTranslations("accountMenu");
  const [open, setOpen] = useState(false);
  const toggle = useRef<HTMLButtonElement>(null);

  return (
    <nav
      aria-label={t("label")}
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
        {t("toggle")}
        <ChevronDownIcon aria-hidden="true" className={open ? "rotate-180" : ""} />
      </Button>
      <div
        id={MENU_ID}
        className={`${open ? "flex" : "hidden"} absolute top-full right-0 z-40 mt-2 w-64 flex-col items-stretch gap-1 rounded-md border bg-background p-2 shadow-md sm:static sm:mt-0 sm:flex sm:w-auto sm:flex-row sm:items-center sm:gap-3 sm:border-0 sm:p-0 sm:shadow-none`}
      >
        <p className="px-2 py-1 text-sm sm:p-0">
          {t.rich("greeting", { name: accountName, strong: (chunks) => <span className="font-medium">{chunks}</span> })}
        </p>
        {links.map((link) => (
          <Link
            key={link.href}
            href={link.href}
            onClick={() => setOpen(false)}
            className="flex min-h-11 items-center rounded-md px-2 text-sm underline-offset-4 hover:bg-accent sm:order-first sm:min-h-0 sm:p-0 sm:hover:bg-transparent sm:hover:underline"
          >
            {link.label}
          </Link>
        ))}
        <LogoutButton size="cta" />
      </div>
    </nav>
  );
}
