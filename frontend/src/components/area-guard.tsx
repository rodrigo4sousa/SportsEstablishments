import type { ReactNode } from "react";
import { headers } from "next/headers";
import { redirect } from "next/navigation";
import { AreaHeader } from "@/components/area-header";
import { areaDecision, profileHome, type Profile } from "@/lib/auth/redirects";
import { REQUEST_PATH_HEADER } from "@/lib/auth/request-path";
import { getSessionAccount } from "@/lib/server/session";

type AreaGuardProps = {
  profile: Profile;
  title: string;
  children: ReactNode;
};

/**
 * Server-side guard of an area layout (US03): no session goes to `/login`, another profile goes to its own area.
 * Without a session, the visitor comes back to the page they asked for (path from the proxy's header, validated by
 * `safeNext`). Reads the request's cookies, so the layout must render it inside a `<Suspense>` boundary (Cache Components).
 * The API enforces access on its own; this only keeps people out of pages that cannot work for them.
 */
export async function AreaGuard({ profile, title, children }: AreaGuardProps) {
  const account = await getSessionAccount();
  const requestedPath = (await headers()).get(REQUEST_PATH_HEADER);
  const decision = areaDecision(profile, account, requestedPath);
  if (!decision.allowed) {
    redirect(decision.redirectTo);
  }

  return (
    <>
      <AreaHeader title={title} homeHref={profileHome(profile)} accountName={decision.account.name} />
      <main className="flex flex-1 flex-col">{children}</main>
    </>
  );
}
