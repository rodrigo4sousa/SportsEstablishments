import type { ReactNode } from "react";
import { Main } from "@/components/main";
import { PAGE_WIDTH } from "@/components/page-width";
import { PublicHeader } from "@/components/public-header";

/** T07: the frame shared by the Terms and the Privacy Policy. No account links: these pages ask the API for nothing. */
export default function Layout({ children }: { children: ReactNode }) {
  return (
    <>
      <PublicHeader account={false} />
      <Main className={`mx-auto flex w-full ${PAGE_WIDTH.narrow} flex-1 flex-col px-4 py-12`}>{children}</Main>
    </>
  );
}
