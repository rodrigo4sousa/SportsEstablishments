import type { ReactNode } from "react";
import { redirect } from "next/navigation";
import { profileHome } from "@/lib/auth/redirects";
import { getSessionAccount } from "@/lib/server/session";

/**
 * For the pages that only make sense without a session (login and the two sign-up pages): a signed-in user is sent
 * to the home of their profile, decided on the server. Reads the request's cookies, so the page renders it inside a
 * `<Suspense>` boundary (Cache Components). A user whose access token has expired is not signed in here; the login
 * page then renews the session silently in the browser.
 */
export async function GuestOnly({ children }: { children: ReactNode }) {
  const account = await getSessionAccount();
  if (account) {
    redirect(profileHome(account.profile));
  }

  return children;
}
