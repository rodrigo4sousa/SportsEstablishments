/** Longest slug the API makes: 80 for the name part plus a numeric suffix (US06). */
const SLUG_MAX_LENGTH = 91;

const SLUG = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

/**
 * True for something the API could have made as a club slug (lower-case letters and digits joined by hyphens). Anything
 * else is a club that cannot exist, with no need to ask the API.
 */
export function isClubSlug(value: string): boolean {
  return value.length <= SLUG_MAX_LENGTH && SLUG.test(value);
}
