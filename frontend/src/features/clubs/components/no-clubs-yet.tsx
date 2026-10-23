import Link from "next/link";
import { Button } from "@/components/ui/button";

/** The owner's home while they have no club (US02, US06): invites them to create the first one. */
export function NoClubsYet() {
  return (
    <section className="flex flex-col gap-4">
      <h2 className="text-2xl font-semibold tracking-tight">Ainda não tens nenhum clube</h2>
      <p className="max-w-prose text-muted-foreground">
        Cria o teu primeiro clube para definir os campos, os horários e começar a receber reservas.
      </p>
      <div>
        <Button asChild>
          <Link href="/manage/clubs/new">Criar o primeiro clube</Link>
        </Button>
      </div>
    </section>
  );
}
