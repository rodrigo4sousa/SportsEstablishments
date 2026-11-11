import type { Messages, NamespaceKeys, NestedKeyOf, useTranslations } from "next-intl";

/** A namespace of the catalogs, e.g. `"validation"` or `"pitches.form"`. */
export type Namespace = NamespaceKeys<Messages, NestedKeyOf<Messages>>;

/**
 * The translator of one namespace, as `useTranslations(namespace)` gives it. Zod schemas and other code outside React take one as a
 * parameter, so their messages are in the page's language (T09).
 */
export type Translator<NS extends Namespace> = ReturnType<typeof useTranslations<NS>>;
