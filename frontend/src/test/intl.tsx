import type { ReactElement, ReactNode } from "react";
import { render } from "@testing-library/react";
import { createTranslator, NextIntlClientProvider, type Messages, type NamespaceKeys, type NestedKeyOf } from "next-intl";
import en from "../../messages/en.json";
import pt from "../../messages/pt-PT.json";
import type { AppLocale } from "@/i18n/locales";
import { LISBON_TIME_ZONE } from "@/lib/datetime";

/** The real catalogs: a component test reads the same words a person sees (pt-PT unless a test says otherwise). */
export const TEST_MESSAGES: Record<AppLocale, Messages> = { "pt-PT": pt, en };

/** The provider the root layout puts around every page, with the real catalog of one language. */
export function IntlProvider({ locale = "pt-PT", children }: { locale?: AppLocale; children: ReactNode }) {
  return (
    <NextIntlClientProvider locale={locale} messages={TEST_MESSAGES[locale]} timeZone={LISBON_TIME_ZONE}>
      {children}
    </NextIntlClientProvider>
  );
}

/** `render` inside {@link IntlProvider}, for a component that needs nothing else. */
export function renderWithIntl(ui: ReactElement, locale: AppLocale = "pt-PT") {
  return render(ui, { wrapper: ({ children }) => <IntlProvider locale={locale}>{children}</IntlProvider> });
}

/** A translator outside React (a Zod schema factory, a pure function), like the one a component gets from `useTranslations`. */
export function testTranslator<Namespace extends NamespaceKeys<Messages, NestedKeyOf<Messages>>>(namespace: Namespace, locale: AppLocale = "pt-PT") {
  return createTranslator({ locale, messages: TEST_MESSAGES[locale], namespace });
}
