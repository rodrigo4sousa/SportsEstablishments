import Link from "next/link";
import { Button } from "@/components/ui/button";

/**
 * Shown for a club that does not exist and for one that is hidden (no active pitch, or its owner is deactivated):
 * the API answers the same 404, and the page does not tell them apart either.
 */
export function PublicClubNotFound() {
  return (
    <section className="flex flex-col items-start gap-4">
      <h2 className="text-2xl font-semibold tracking-tight">Clube não encontrado</h2>
      <p className="max-w-prose text-muted-foreground">Este clube não existe ou já não está disponível.</p>
      <Button asChild variant="outline">
        <Link href="/">Pesquisar clubes</Link>
      </Button>
    </section>
  );
}
