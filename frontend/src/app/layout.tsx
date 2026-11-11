import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getTranslations } from "next-intl/server";
import { Providers } from "@/components/providers";
import { SiteFooter } from "@/components/site-footer";
import { SkipLink } from "@/components/skip-link";
import { htmlLang } from "@/i18n/locales";
import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

/**
 * The language is read from the request here (`getLocale`: the `NEXT_LOCALE` cookie, then `Accept-Language`), above every page, so no
 * page can have a prerendered shell: a shell is one HTML in one language. This marks the whole tree as allowed to block on the request
 * (ADR 0012); the pages still stream below their `loading.tsx` and Suspense boundaries.
 */
export const instant = false;

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("metadata");
  return {
    // One suffix for every page; a page sets only its own part (UX-011).
    title: { default: t("siteName"), template: t("titleTemplate") },
    description: t("description"),
  };
}

export default async function RootLayout({ children }: LayoutProps<"/">) {
  // The language of this request (cookie, then Accept-Language, then pt-PT): it makes every page request-bound (ADR 0012).
  const locale = await getLocale();
  return (
    <html
      lang={htmlLang(locale)}
      className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}
    >
      <body className="min-h-full flex flex-col">
        <NextIntlClientProvider>
          <SkipLink />
          <Providers>{children}</Providers>
          <SiteFooter />
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
