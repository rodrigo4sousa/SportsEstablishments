import { useTranslations } from "next-intl";
import { useSportLabel } from "@/features/pitches/sports";
import { useFormats } from "@/i18n/formats";
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
  const t = useTranslations("catalog.details");
  return (
    <section aria-labelledby="club-contacts" className="flex flex-col gap-3">
      <h2 id="club-contacts" className="text-lg font-semibold">
        {t("contacts")}
      </h2>
      <dl className="grid gap-x-6 gap-y-2 sm:grid-cols-[max-content_1fr]">
        <dt className="text-muted-foreground">{t("address")}</dt>
        <dd>
          {club.address}, {club.locality}
        </dd>
        <dt className="text-muted-foreground">{t("phone")}</dt>
        <dd>
          <a href={`tel:${club.phone}`} className="underline underline-offset-4">
            {club.phone}
          </a>
        </dd>
        <dt className="text-muted-foreground">{t("email")}</dt>
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
  const t = useTranslations("catalog.details");
  const sportLabel = useSportLabel();
  const formats = useFormats();
  return (
    <section aria-labelledby="club-pitches" className="flex flex-col gap-3">
      <h2 id="club-pitches" className="text-lg font-semibold">
        {t("pitches")}
      </h2>
      {club.pitches.length === 0 ? (
        <p className="text-muted-foreground">{t("noPitches")}</p>
      ) : (
        <ul aria-label={t("pitches")} className="flex flex-col gap-2">
          {club.pitches.map((pitch) => (
            <li key={pitch.id} className="flex flex-wrap items-baseline justify-between gap-x-4 rounded-lg border px-4 py-3">
              <span>
                <span className="font-medium">{pitch.name}</span> <span className="text-muted-foreground">· {sportLabel(pitch.sport)}</span>
              </span>
              <span className="tabular-nums">
                {t(pitch.hasPriceBands ? "basePrice" : "price", { price: formats.price(pitch.pricePerSlot) })}
              </span>
              {pitch.hasPriceBands ? (
                <span className="basis-full text-sm text-muted-foreground">{t("priceBandsNote")}</span>
              ) : null}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
