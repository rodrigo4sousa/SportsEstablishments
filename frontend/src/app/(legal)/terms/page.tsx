import type { Metadata } from "next";
import { useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { LEGAL_TAGS, LegalDocument, LegalList, LegalSection } from "@/features/legal/components/legal-document";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("legal.terms");
  return { title: t("metaTitle"), description: t("description") };
}

/** T07, T09: the Terms, in the page's language; the English text is a courtesy translation (`legal.languageNote`). */
export default function Page() {
  const t = useTranslations("legal.terms");
  return (
    <LegalDocument title={t("metaTitle")}>
      <p>{t("intro")}</p>

      <LegalSection title={t("service.title")}>
        <p>{t("service.p1")}</p>
        <LegalList>
          <li>{t("service.fictional")}</li>
          <li>{t("service.free")}</li>
          <li>{t("service.noBackups")}</li>
          <li>{t("service.sleeps")}</li>
        </LegalList>
      </LegalSection>

      <LegalSection title={t("accounts.title")}>
        <LegalList>
          <li>{t("accounts.profiles")}</li>
          <li>{t("accounts.everywhere")}</li>
          <li>{t("accounts.secret")}</li>
          <li>{t("accounts.demo")}</li>
          <li>{t("accounts.deactivation")}</li>
        </LegalList>
      </LegalSection>

      <LegalSection title={t("bookings.title")}>
        <LegalList>
          <li>{t("bookings.window")}</li>
          <li>{t("bookings.payments")}</li>
          <li>{t("bookings.cancel")}</li>
          <li>{t("bookings.clubCancel")}</li>
          <li>{t("bookings.noShow")}</li>
          <li>{t("bookings.weekly")}</li>
          <li>{t("bookings.neverDeleted")}</li>
        </LegalList>
      </LegalSection>

      <LegalSection title={t("owners.title")}>
        <LegalList>
          <li>{t("owners.data")}</li>
          <li>{t.rich("owners.customers", LEGAL_TAGS)}</li>
        </LegalList>
      </LegalSection>

      <LegalSection title={t("use.title")}>
        <p>{t("use.intro")}</p>
        <LegalList>
          <li>{t("use.access")}</li>
          <li>{t("use.overload")}</li>
          <li>{t("use.impersonate")}</li>
          <li>{t("use.content")}</li>
        </LegalList>
      </LegalSection>

      <LegalSection title={t("availability.title")}>
        <p>{t("availability.p1")}</p>
        <p>{t("availability.p2")}</p>
      </LegalSection>

      <LegalSection title={t("changes.title")}>
        <p>{t("changes.p1")}</p>
      </LegalSection>

      <LegalSection title={t("law.title")}>
        <p>{t("law.p1")}</p>
      </LegalSection>

      <LegalSection title={t("contact.title")}>
        <p>{t.rich("contact.p1", LEGAL_TAGS)}</p>
      </LegalSection>
    </LegalDocument>
  );
}
