import { Button } from "@/components/ui/button";

/**
 * The owner's home while they have no club (US02): invites them to create the first one.
 * Placeholder: club creation is US06, so there is no club list yet (every owner sees this) and the button does
 * nothing. Replace the button with a link to the create-club form, and show this only for an empty list.
 */
export function NoClubsYet() {
  return (
    <section className="mx-auto flex w-full max-w-3xl flex-1 flex-col gap-4 px-4 py-12">
      <h1 className="text-2xl font-semibold tracking-tight">Ainda não tens nenhum clube</h1>
      <p className="max-w-prose text-muted-foreground">
        Cria o teu primeiro clube para definir os campos, os horários e começar a receber reservas.
      </p>
      <div className="flex flex-col items-start gap-2">
        <Button type="button" disabled aria-describedby="no-clubs-yet-hint">
          Criar o primeiro clube
        </Button>
        <p id="no-clubs-yet-hint" className="text-sm text-muted-foreground">
          Em breve: a criação de clubes ainda não está disponível.
        </p>
      </div>
    </section>
  );
}
