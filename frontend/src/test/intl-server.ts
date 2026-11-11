import { createTranslator } from "next-intl";
import type { AppLocale } from "@/i18n/locales";
import { LISBON_TIME_ZONE } from "@/lib/datetime";
import { TEST_MESSAGES } from "@/test/intl";

/**
 * Stands in for `next-intl/server` in component tests (vitest.setup.ts mocks it with this module): outside a Next.js request there is no
 * request config to read, so the language is whatever the test set, pt-PT by default, with the real catalogs.
 */
let locale: AppLocale = "pt-PT";

export function setServerLocale(next: AppLocale) {
  locale = next;
}

export function resetServerLocale() {
  locale = "pt-PT";
}

type NamespaceArg = string | { locale?: AppLocale; namespace?: string } | undefined;

export async function getTranslations(arg?: NamespaceArg) {
  const options = typeof arg === "string" ? { namespace: arg } : (arg ?? {});
  const language = options.locale ?? locale;
  // The namespace is checked by the real `getTranslations` types where it is called; here any string goes.
  return createTranslator({ locale: language, messages: TEST_MESSAGES[language], namespace: options.namespace as never });
}

/** The real one only registers the function for Next.js; a test calls it directly. */
export function getRequestConfig<T>(create: T): T {
  return create;
}

export async function getLocale(): Promise<AppLocale> {
  return locale;
}

export async function getMessages() {
  return TEST_MESSAGES[locale];
}

export async function getTimeZone() {
  return LISBON_TIME_ZONE;
}
