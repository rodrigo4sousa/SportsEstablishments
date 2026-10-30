import Link from "next/link";
import { Main } from "@/components/main";
import { PAGE_WIDTH } from "@/components/page-width";
import { PublicHeader } from "@/components/public-header";
import { Button } from "@/components/ui/button";

/** UX-007: the page for an address that does not exist, in pt-PT and with the way back, instead of Next.js's English default. */
export default function NotFound() {
  return (
    <>
      {/* Not a `metadata` export: not-found pages do not take one, and React puts a `<title>` in the head. */}
      <title>Página não encontrada · Reservas de Campos</title>
      <PublicHeader />
      <Main className={`mx-auto flex w-full ${PAGE_WIDTH.narrow} flex-1 flex-col items-start gap-4 px-4 py-12`}>
        <h1 className="text-2xl font-semibold tracking-tight">Página não encontrada</h1>
        <p className="text-muted-foreground">Este endereço não existe ou já não está disponível.</p>
        <div className="flex flex-wrap items-center gap-3">
          <Button asChild>
            <Link href="/">Pesquisar clubes</Link>
          </Button>
        </div>
      </Main>
    </>
  );
}
