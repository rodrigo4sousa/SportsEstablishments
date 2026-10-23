import type { ReactNode } from "react";
import { redirect } from "next/navigation";
import { AreaHeader } from "@/components/area-header";
import { areaDecision, profileHome, type Profile } from "@/lib/auth/redirects";
import { getSessionAccount } from "@/lib/server/session";

type AreaGuardProps = {
  profile: Profile;
  title: string;
  children: ReactNode;
};

/**
 * Server-side guard of an area layout (US03): no session goes to `/login`, another profile goes to its own area.
 * Reads the request's cookies, so the layout must render it inside a `<Suspense>` boundary (Cache Components).
 * The API enforces access on its own; this only keeps people out of pages that cannot work for them.
 */
export async function AreaGuard({ profile, title, children }: AreaGuardProps) {
  const account = await getSessionAccount();
  const decision = areaDecision(profile, account);
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
