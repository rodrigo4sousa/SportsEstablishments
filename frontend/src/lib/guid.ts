const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Public ids are GUIDs; anything else cannot be a resource of ours, so there is no need to ask the API. */
export function isGuid(value: string): boolean {
  return GUID_PATTERN.test(value);
}
