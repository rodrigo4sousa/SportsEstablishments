import type { Metadata } from "next";
import { useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { LEGAL_TAGS, LegalDocument, LegalList, LegalSection, LegalSubsection } from "@/features/legal/components/legal-document";
import { DATA_CONTROLLER_NAME } from "@/features/legal/legal-info";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("legal.privacy");
  return { title: t("metaTitle"), description: t("description") };
}

const PURPOSES = ["account", "sessions", "bookings", "clubs", "emails", "language", "ip", "logs"] as const;

/** T07, T09: the Privacy Policy, in the page's language; the English text is a courtesy translation (`legal.languageNote`). */
export default function Page() {
  const t = useTranslations("legal.privacy");
  return (
    <LegalDocument title={t("metaTitle")}>
      <p>{t("intro")}</p>

      <LegalSection title={t("controller.title")}>
        <p>{t.rich("controller.p1", { ...LEGAL_TAGS, name: DATA_CONTROLLER_NAME })}</p>
      </LegalSection>

      <LegalSection title={t("data.title")}>
        {PURPOSES.map((purpose) => (
          <LegalSubsection key={purpose} title={t(`data.${purpose}.title`)}>
            <p>{t(`data.${purpose}.what`)}</p>
            <p>{t(`data.${purpose}.why`)}</p>
            {purpose === "clubs" ? <p>{t("data.clubs.admin")}</p> : null}
          </LegalSubsection>
        ))}
      </LegalSection>

      <LegalSection title={t("customers.title")}>
        <p>{t("customers.p1")}</p>
        <p>{t.rich("customers.p2", LEGAL_TAGS)}</p>
      </LegalSection>

      <LegalSection title={t("recipients.title")}>
        <p>{t("recipients.intro")}</p>
        <LegalList>
          <li>{t.rich("recipients.vercel", LEGAL_TAGS)}</li>
          <li>{t.rich("recipients.render", LEGAL_TAGS)}</li>
          <li>{t.rich("recipients.supabase", LEGAL_TAGS)}</li>
          <li>{t.rich("recipients.brevo", LEGAL_TAGS)}</li>
          <li>{t.rich("recipients.github", LEGAL_TAGS)}</li>
        </LegalList>
      </LegalSection>

      <LegalSection title={t("transfers.title")}>
        <p>{t("transfers.p1")}</p>
        <p>{t("transfers.p2")}</p>
        <p>{t("transfers.p3")}</p>
        <LegalList>
          <li>{t.rich("transfers.vercel", LEGAL_TAGS)}</li>
          <li>{t.rich("transfers.render", LEGAL_TAGS)}</li>
          <li>{t.rich("transfers.supabase", LEGAL_TAGS)}</li>
          <li>{t.rich("transfers.brevo", LEGAL_TAGS)}</li>
        </LegalList>
      </LegalSection>

      <LegalSection title={t("retention.title")}>
        <LegalList>
          <li>{t("retention.noLimit")}</li>
          <li>{t("retention.deactivated")}</li>
          <li>{t("retention.counters")}</li>
          <li>{t("retention.logs")}</li>
          <li>{t("retention.languageCookie")}</li>
          <li>{t("retention.noBackups")}</li>
        </LegalList>
      </LegalSection>

      <LegalSection title={t("cookies.title")}>
        <p>{t("cookies.intro")}</p>
        <p>{t("cookies.session")}</p>
        <LegalList>
          <li>{t.rich("cookies.access", LEGAL_TAGS)}</li>
          <li>{t.rich("cookies.refresh", LEGAL_TAGS)}</li>
          <li>{t.rich("cookies.locale", LEGAL_TAGS)}</li>
        </LegalList>
        <p>{t("cookies.noStorage")}</p>
      </LegalSection>

      <LegalSection title={t("rights.title")}>
        <p>{t("rights.intro")}</p>
        <LegalList>
          <li>{t.rich("rights.access", LEGAL_TAGS)}</li>
          <li>{t.rich("rights.erasure", LEGAL_TAGS)}</li>
          <li>{t.rich("rights.owners", LEGAL_TAGS)}</li>
        </LegalList>
        <p>{t.rich("rights.other", LEGAL_TAGS)}</p>
      </LegalSection>

      <LegalSection title={t("complaints.title")}>
        <p>{t("complaints.p1")}</p>
      </LegalSection>

      <LegalSection title={t("changes.title")}>
        <p>{t("changes.p1")}</p>
      </LegalSection>
    </LegalDocument>
  );
}
