import { sportLabel } from "@/features/pitches/sports";
import { formatPrice } from "@/features/pitches/price";
import type { PublicClub } from "../types";

/** US16: the contacts of a club and its active pitches with their current price per slot. Presentational only. */
export function ClubDetails({ club }: { club: PublicClub }) {
  return (
    <div className="flex flex-col gap-8">
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
                <span className="tabular-nums">{formatPrice(pitch.pricePerSlot)} por slot</span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
