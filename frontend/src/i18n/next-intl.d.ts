import type messages from "../../messages/pt-PT.json";
import type { AppLocale } from "./locales";

// Typed keys (a missing or misspelt key fails `npm run typecheck`) and typed locales. pt-PT is the reference catalog; `messages.test.ts`
// checks that en.json has exactly the same keys and placeholders.
declare module "next-intl" {
  interface AppConfig {
    Locale: AppLocale;
    Messages: typeof messages;
  }
}
