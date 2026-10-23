import Link from "next/link";
import { Button } from "@/components/ui/button";

/**
 * Shown for a club that does not exist and for a club of another owner alike (T02: the API answers the same 404 for
 * both, and the page must not tell them apart either).
 */
export function ClubNotFound() {
  return (
    <section className="flex flex-col items-start gap-4">
      <h2 className="text-2xl font-semibold tracking-tight">Clube não encontrado</h2>
      <p className="max-w-prose text-muted-foreground">Este clube não existe ou não é teu.</p>
      <Button asChild variant="outline">
        <Link href="/manage">Voltar aos clubes</Link>
      </Button>
    </section>
  );
}
