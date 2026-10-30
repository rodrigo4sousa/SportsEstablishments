import { sportLabel } from "@/features/pitches/sports";
import { formatPrice } from "@/features/pitches/price";
import type { PublicClub } from "../types";

/** US16: the contacts and the pitches of a club. The page puts the slots first and these after them (UX-010); `ClubDetails` is both, in the old order. */
export function ClubDetails({ club }: { club: PublicClub }) {
  return (
    <div className="flex flex-col gap-8">
      <ClubContacts club={club} />
      <ClubPitches club={club} />
    </div>
  );
}

/** US16: where the club is and how to reach it. */
export function ClubContacts({ club }: { club: PublicClub }) {
  return (
    <section aria-labelledby="club-contacts" className="flex flex-col gap-3">
      <h2 id="club-contacts" className="text-lg font-semibold">
        Contactos
      </h2>
      <dl className="grid gap-x-6 gap-y-2 sm:grid-cols-[max-content_1fr]">
        <dt className="text-muted-foreground">Morada</dt>
        <dd>
          {club.address}, {club.locality}
        </dd>
        <dt className="text-muted-foreground">Telefone</dt>
        <dd>
          <a href={`tel:${club.phone}`} className="underline underline-offset-4">
            {club.phone}
          </a>
        </dd>
        <dt className="text-muted-foreground">Email</dt>
        <dd>
          <a href={`mailto:${club.contactEmail}`} className="underline underline-offset-4">
            {club.contactEmail}
          </a>
        </dd>
      </dl>
    </section>
  );
}

/** US16: the club's active pitches with their current price per slot. */
export function ClubPitches({ club }: { club: PublicClub }) {
  return (
    <section aria-labelledby="club-pitches" className="flex flex-col gap-3">
      <h2 id="club-pitches" className="text-lg font-semibold">
        Campos
      </h2>
      {club.pitches.length === 0 ? (
        <p className="text-muted-foreground">Este clube não tem campos disponíveis de momento.</p>
      ) : (
        <ul aria-label="Campos" className="flex flex-col gap-2">
          {club.pitches.map((pitch) => (
            <li key={pitch.id} className="flex flex-wrap items-baseline justify-between gap-x-4 rounded-lg border px-4 py-3">
              <span>
                <span className="font-medium">{pitch.name}</span> <span className="text-muted-foreground">· {sportLabel(pitch.sport)}</span>
              </span>
              <span className="tabular-nums">
                {pitch.hasPriceBands ? "Preço base: " : ""}
                {formatPrice(pitch.pricePerSlot)} por horário
              </span>
              {pitch.hasPriceBands ? (
                <span className="basis-full text-sm text-muted-foreground">Alguns horários têm outro preço; o preço de cada horário aparece na escolha do horário.</span>
              ) : null}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
