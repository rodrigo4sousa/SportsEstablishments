import Link from "next/link";
import { PAGE_WIDTH } from "@/components/page-width";
import { PRIVACY_PATH, TERMS_PATH } from "@/features/legal/legal-info";

const LINK_CLASS = "inline-block py-1 underline underline-offset-4";

/** T07: the links to the legal pages, in the root layout so that every page has them. */
export function SiteFooter() {
  return (
    <footer className="mt-auto border-t">
      <nav aria-label="Informação legal" className={`mx-auto flex w-full ${PAGE_WIDTH.narrow} flex-wrap gap-x-6 gap-y-1 px-4 py-3 text-sm text-muted-foreground`}>
        <Link href={TERMS_PATH} className={LINK_CLASS}>
          Termos e Condições
        </Link>
        <Link href={PRIVACY_PATH} className={LINK_CLASS}>
          Política de Privacidade
        </Link>
      </nav>
    </footer>
  );
}
