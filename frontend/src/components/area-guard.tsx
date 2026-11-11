import type { ReactNode } from "react";
import { headers } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import type { AccountMenuLink } from "@/components/account-menu";
import { AreaHeader } from "@/components/area-header";
import { Main } from "@/components/main";
import type { PageWidth } from "@/components/page-width";
import { areaDecision, profileHome, type Profile } from "@/lib/auth/redirects";
import { REQUEST_PATH_HEADER } from "@/lib/auth/request-path";
import { getSessionAccount } from "@/lib/server/session";

const AREA_LINKS: Record<Profile, ReadonlyArray<{ href: string; label: "myClubs" | "statistics" | "myAccount" }>> = {
  Player: [],
  Owner: [
    { href: "/manage", label: "myClubs" },
    { href: "/manage/statistics", label: "statistics" },
    { href: "/account", label: "myAccount" },
  ],
  Admin: [],
};

type AreaGuardProps = {
  profile: Profile;
  title: string;
  /** The width of the area's pages; the header uses the same one. */
  width?: PageWidth;
  children: ReactNode;
};

/**
 * Server-side guard of an area layout (US03): no session goes to `/login`, another profile goes to its own area.
 * Without a session, the visitor comes back to the page they asked for (path from the proxy's header, validated by
 * `safeNext`). Reads the request's cookies, so the layout must render it inside a `<Suspense>` boundary (Cache Components).
 * The API enforces access on its own; this only keeps people out of pages that cannot work for them.
 */
export async function AreaGuard({ profile, title, width, children }: AreaGuardProps) {
  const account = await getSessionAccount();
  const requestedPath = (await headers()).get(REQUEST_PATH_HEADER);
  const decision = areaDecision(profile, account, requestedPath);
  if (!decision.allowed) {
    redirect(decision.redirectTo);
  }

  const t = await getTranslations("areaLinks");
  const links: AccountMenuLink[] = AREA_LINKS[profile].map((link) => ({ href: link.href, label: t(link.label) }));
  return (
    <>
      <AreaHeader title={title} homeHref={profileHome(profile)} accountName={decision.account.name} width={width} links={links} />
      <Main>{children}</Main>
    </>
  );
}
