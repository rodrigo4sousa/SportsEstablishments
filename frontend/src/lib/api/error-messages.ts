import { NETWORK_ERROR } from "./client";

/** User-facing message for each stable API error `code` (docs/stack.md: the front end picks the message by code). */
const MESSAGES: Record<string, string> = {
  invalid_credentials: "Email ou palavra-passe incorretos.",
  club_not_found: "Este clube não existe ou não é teu.",
  email_taken: "Já existe uma conta com este email.",
  validation_failed: "Há campos com erros. Corrige-os e tenta novamente.",
  origin_not_allowed: "O pedido foi recusado. Recarrega a página e tenta novamente.",
  too_many_requests: "Fizeste demasiados pedidos seguidos. Espera um pouco e tenta novamente.",
  [NETWORK_ERROR]: "Não foi possível contactar o servidor. Verifica a ligação e tenta novamente.",
};

const FALLBACK = "Ocorreu um erro inesperado. Tenta novamente mais tarde.";

export function errorMessage(code: string): string {
  return MESSAGES[code] ?? FALLBACK;
}
