import { describe, expect, it } from "vitest";
import { z } from "zod";
import { testTranslator } from "@/test/intl";
import { createRegisterSchema, type RegisterFormValues } from "./register";

const registerSchema = createRegisterSchema(testTranslator("validation"), testTranslator("apiErrors")("terms_not_accepted"));

const valid: RegisterFormValues = { name: "Ana Silva", email: "ana@exemplo.pt", password: "palavra-passe", phone: "", acceptTerms: true };

function errorsFor(values: Partial<RegisterFormValues>) {
  const result = registerSchema.safeParse({ ...valid, ...values });
  return result.success ? {} : z.flattenError(result.error).fieldErrors;
}

describe("registerSchema", () => {
  it("accepts a complete form and normalizes it", () => {
    expect(registerSchema.parse({ name: "  Ana  ", email: " ana@exemplo.pt ", password: " palavra ", phone: "+351 912 345 678", acceptTerms: true })).toEqual({
      name: "Ana",
      email: "ana@exemplo.pt",
      password: " palavra ",
      phone: "+351912345678",
      acceptTerms: true,
    });
  });

  it("requires the terms to be accepted", () => {
    expect(errorsFor({ acceptTerms: false }).acceptTerms).toEqual([
      "Para criar conta, tens de aceitar os Termos e Condições e tomar conhecimento da Política de Privacidade.",
    ]);
  });

  it("treats a blank phone as not given", () => {
    expect(registerSchema.parse({ ...valid, phone: "   " }).phone).toBeNull();
  });

  it.each(["", "   "])("requires the name (%j)", (name) => {
    expect(errorsFor({ name }).name).toEqual(["Indica o teu nome."]);
  });

  it("limits the name to 100 characters", () => {
    expect(errorsFor({ name: "a".repeat(100) }).name).toBeUndefined();
    expect(errorsFor({ name: "a".repeat(101) }).name).toEqual(["O nome pode ter no máximo 100 caracteres."]);
  });

  it("requires the email with its own message", () => {
    expect(errorsFor({ email: "  " }).email).toEqual(["Indica o teu email."]);
  });

  it.each(["ana", "ana@", "@exemplo.pt", "ana@localhost", "ana silva@exemplo.pt"])("rejects the malformed email %j", (email) => {
    expect(errorsFor({ email }).email).toEqual(["Indica um email válido, por exemplo nome@exemplo.pt."]);
  });

  it("accepts addresses with an apostrophe or a plus sign", () => {
    expect(errorsFor({ email: "o'neill+reservas@exemplo.pt" }).email).toBeUndefined();
  });

  it.each(["", "1234567"])("needs a password with at least 8 characters (%j)", (password) => {
    expect(errorsFor({ password }).password).toEqual(["A palavra-passe tem de ter pelo menos 8 caracteres."]);
  });

  it.each(["12345678", "        ", "só letras"])("accepts any 8-character password (%j)", (password) => {
    expect(errorsFor({ password }).password).toBeUndefined();
  });

  it.each(["12345678", "1234567890123456", "91234567a", "912-345-678"])("rejects the phone %j", (phone) => {
    expect(errorsFor({ phone }).phone).toEqual(["Indica um número de telemóvel válido, com 9 a 15 algarismos."]);
  });
});

describe("registerSchema in English (T09)", () => {
  const english = createRegisterSchema(testTranslator("validation", "en"), testTranslator("apiErrors", "en")("terms_not_accepted"));

  it("says what is wrong in English", () => {
    const result = english.safeParse({ ...valid, name: "", password: "short" });
    const errors = result.success ? {} : z.flattenError(result.error).fieldErrors;
    expect(errors.name).toEqual(["Enter your name."]);
    expect(errors.password).toEqual(["The password must have at least 8 characters."]);
  });
});
