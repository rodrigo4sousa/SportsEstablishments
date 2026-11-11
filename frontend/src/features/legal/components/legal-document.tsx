import { useId, type ReactNode } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { formatDateKey } from "@/lib/datetime";
import { DATA_PROTECTION_EMAIL, LEGAL_LAST_UPDATED, LEGAL_VERSION, PRIVACY_PATH } from "../legal-info";

/**
 * The frame of a legal page: its one `h1`, then when it was last changed and which version it is, and (T09) that the English text is a
 * courtesy translation and the Portuguese one prevails.
 */
export function LegalDocument({ title, children }: { title: string; children: ReactNode }) {
  const t = useTranslations("legal");
  return (
    <article className="flex flex-col gap-8">
      <header className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
        <p className="text-sm text-muted-foreground">{t("lastUpdated", { date: formatDateKey(LEGAL_LAST_UPDATED), version: LEGAL_VERSION })}</p>
        <p className="text-sm text-muted-foreground">{t("languageNote")}</p>
      </header>
      {children}
    </article>
  );
}

/** One titled section; a region named by its `h2`, so a screen reader can jump between them. */
export function LegalSection({ title, children }: { title: string; children: ReactNode }) {
  const headingId = useId();

  return (
    <section aria-labelledby={headingId} className="flex flex-col gap-3">
      <h2 id={headingId} className="text-lg font-semibold tracking-tight">
        {title}
      </h2>
      {children}
    </section>
  );
}

export function LegalSubsection({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-2">
      <h3 className="font-medium">{title}</h3>
      {children}
    </div>
  );
}

export function LegalList({ children }: { children: ReactNode }) {
  return <ul className="flex list-disc flex-col gap-2 pl-5">{children}</ul>;
}

export function ContactEmailLink() {
  return (
    <a href={`mailto:${DATA_PROTECTION_EMAIL}`} className="underline underline-offset-4">
      {DATA_PROTECTION_EMAIL}
    </a>
  );
}

/** The tags the legal texts in the catalogs use: `<email></email>`, `<b>`, `<code>` and `<link>` (to the Privacy Policy). */
export const LEGAL_TAGS = {
  email: () => <ContactEmailLink />,
  b: (chunks: ReactNode) => <strong className="font-medium">{chunks}</strong>,
  code: (chunks: ReactNode) => <code>{chunks}</code>,
  link: (chunks: ReactNode) => (
    <Link href={PRIVACY_PATH} className="underline underline-offset-4">
      {chunks}
    </Link>
  ),
};
