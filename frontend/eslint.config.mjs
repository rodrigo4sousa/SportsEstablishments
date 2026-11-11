import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";

// T09: the attributes a person reads or hears. A string written straight into one of them is text that is not in the catalogs.
const TEXT_ATTRIBUTES = "/^(placeholder|aria-label|title|alt)$/";
// A string with a letter in it (punctuation and symbols such as "·" or "|" are not text to translate).
const HAS_LETTER = "/\\p{L}/u";

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  {
    // T09 (ADR 0012): every user-visible string comes from messages/pt-PT.json and messages/en.json. This catches the common slips: text
    // written between JSX tags, and a literal in the attributes above. It does not see strings passed through props or built in code;
    // the review and the catalog test (src/i18n/messages.test.ts) cover those.
    files: ["src/**/*.tsx"],
    ignores: ["src/**/*.test.tsx", "src/test/**"],
    rules: {
      "react/jsx-no-literals": ["error", { noStrings: false, allowedStrings: ["·", "|", "–", "—", "←", "→", "↑", "↓", "#", ".", ",", ":", "(", ")", "€", "%"] }],
      "no-restricted-syntax": [
        "error",
        {
          selector: `JSXAttribute[name.name=${TEXT_ATTRIBUTES}] > Literal[value=${HAS_LETTER}]`,
          message: "User-visible text goes in the message catalogs (messages/*.json), read with useTranslations (T09).",
        },
        {
          selector: `JSXAttribute[name.name=${TEXT_ATTRIBUTES}] > JSXExpressionContainer > Literal[value=${HAS_LETTER}]`,
          message: "User-visible text goes in the message catalogs (messages/*.json), read with useTranslations (T09).",
        },
        {
          selector: `JSXAttribute[name.name=${TEXT_ATTRIBUTES}] > JSXExpressionContainer > TemplateLiteral > TemplateElement[value.raw=${HAS_LETTER}]`,
          message: "User-visible text goes in the message catalogs (messages/*.json), read with useTranslations (T09).",
        },
      ],
    },
  },
  // Override default ignores of eslint-config-next.
  globalIgnores([
    // Default ignores of eslint-config-next:
    ".next/**",
    "out/**",
    "build/**",
    "next-env.d.ts",
  ]),
]);

export default eslintConfig;
