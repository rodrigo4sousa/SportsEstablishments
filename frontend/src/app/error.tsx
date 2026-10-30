"use client";

import { Main } from "@/components/main";
import { PublicHeader } from "@/components/public-header";
import { RouteError } from "@/components/route-error";

/** The error page of any route without its own: with the site header, so there is always a way back (UX-007). */
export default function Error({ error, retry }: { error: unknown; retry: () => void }) {
  return (
    <>
      <PublicHeader />
      <Main>
        <RouteError error={error} retry={retry} />
      </Main>
    </>
  );
}
