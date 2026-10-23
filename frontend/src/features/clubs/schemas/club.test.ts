import { describe, expect, it } from "vitest";
import { clubSchema } from "./club";

const valid = {
  name: "Clube de Ténis da Foz",
  address: "Rua do Mar, 1",
  locality: "Porto",
  phone: "912 345 678",
  contactEmail: "geral@tenisfoz.pt",
  cancellationWindowHours: "24",
};

// What the form shows: react-hook-form keeps the first message of each field.
function messages(values: Partial<Record<keyof typeof valid, string>>) {
  const result = clubSchema.safeParse({ ...valid, ...values });
  const found: Record<string, string[]> = {};
  if (!result.success) {
    for (const issue of result.error.issues) {
      found[String(issue.path[0])] ??= [issue.message];
    }
  }
  return found;
}

describe("clubSchema", () => {
  it("accepts a complete club and normalizes it like the API does", () => {
    const result = clubSchema.parse({
      name: "  Clube  ",
      address: " Rua 1 ",
      locality: " Porto ",
      phone: "+351 912 345 678",
      contactEmail: " geral@clube.pt ",
      cancellationWindowHours: " 48 ",
    });

    expect(result).toEqual({
      name: "Clube",
      address: "Rua 1",
      locality: "Porto",
      phone: "+351912345678",
      contactEmail: "geral@clube.pt",
      cancellationWindowHours: 48,
    });
  });

  it("asks for each missing field with its own message", () => {
    expect(messages({ name: " ", address: "", locality: "", phone: "  ", contactEmail: "", cancellationWindowHours: "" })).toEqual({
      name: ["Indica o nome do clube."],
      address: ["Indica a morada do clube."],
      locality: ["Indica a localidade do clube."],
      phone: ["Indica o telefone do clube."],
      contactEmail: ["Indica o email de contacto do clube."],
      cancellationWindowHours: ["Indica a janela de cancelamento, em horas."],
    });
  });

  it("limits the lengths like the API", () => {
    expect(messages({ name: "a".repeat(101), address: "a".repeat(201), locality: "a".repeat(101) })).toEqual({
      name: ["O nome pode ter no máximo 100 caracteres."],
      address: ["A morada pode ter no máximo 200 caracteres."],
      locality: ["A localidade pode ter no máximo 100 caracteres."],
    });
    expect(messages({ name: "a".repeat(100), address: "a".repeat(200), locality: "a".repeat(100) })).toEqual({});
  });

  it.each(["123", "91234567a", "+3519123456789012"])("rejects the phone %s", (phone) => {
    expect(messages({ phone }).phone).toEqual(["Indica um número de telefone válido, com 9 a 15 algarismos."]);
  });

  it.each(["geral", "geral@clube", "Clube <geral@clube.pt>"])("rejects the contact email %s", (contactEmail) => {
    expect(messages({ contactEmail }).contactEmail).toEqual(["Indica um email válido, por exemplo nome@exemplo.pt."]);
  });

  it.each(["0", "169", "-1", "1.5", "vinte", "24h"])("rejects the cancellation window %s", (cancellationWindowHours) => {
    expect(messages({ cancellationWindowHours }).cancellationWindowHours).toEqual([
      "A janela de cancelamento tem de estar entre 1 e 168 horas.",
    ]);
  });

  it.each(["1", "24", "168"])("accepts the cancellation window %s", (cancellationWindowHours) => {
    expect(messages({ cancellationWindowHours })).toEqual({});
  });
});
