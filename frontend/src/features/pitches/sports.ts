/** The fixed list of sports (US07): the API's stable codes, and the pt-PT label the UI shows for each. */
export const SPORTS = [
  { code: "futsal", label: "Futsal" },
  { code: "football7", label: "Futebol 7" },
  { code: "padel", label: "Padel" },
  { code: "tennis", label: "Ténis" },
] as const;

export type SportCode = (typeof SPORTS)[number]["code"];

export function isSportCode(value: string): value is SportCode {
  return SPORTS.some((sport) => sport.code === value);
}

/** The label of a sport code; an unknown code (a newer API) is shown as it is. */
export function sportLabel(code: string): string {
  return SPORTS.find((sport) => sport.code === code)?.label ?? code;
}
