import { Main } from "@/components/main";
import { PublicHeader } from "@/components/public-header";

export default function Layout({ children }: { children: React.ReactNode }) {
  return (
    <>
      <PublicHeader />
      <Main>{children}</Main>
    </>
  );
}
