import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";

/**
 * UX-001 and UX-008: the colours that failed WCAG are theme variables, so the guarantee lives there too. This reads the real
 * `globals.css`, converts its oklch values to sRGB and checks the pairs the audit and axe flagged, in both themes.
 */

type Rgb = [number, number, number];

const css = readFileSync(join(__dirname, "globals.css"), "utf8");

function block(selector: string): string {
  const start = css.indexOf(`${selector} {`);
  expect(start, `${selector} block`).toBeGreaterThanOrEqual(0);
  return css.slice(start, css.indexOf("\n}", start));
}

function token(scope: string, name: string): Rgb {
  const match = new RegExp(String.raw`--${name}:\s*oklch\(([^)]+)\)`).exec(scope);
  expect(match, `--${name}`).not.toBeNull();
  const [l, c, h = "0"] = match![1].split("/")[0].trim().split(/\s+/);
  return oklchToSrgb(Number(l), Number(c), Number(h));
}

/** oklch → gamma-encoded sRGB in 0..1 (Björn Ottosson's matrices), clipped to the gamut. */
function oklchToSrgb(l: number, c: number, hDegrees: number): Rgb {
  const a = c * Math.cos((hDegrees * Math.PI) / 180);
  const b = c * Math.sin((hDegrees * Math.PI) / 180);
  const l_ = (l + 0.3963377774 * a + 0.2158037573 * b) ** 3;
  const m_ = (l - 0.1055613458 * a - 0.0638541728 * b) ** 3;
  const s_ = (l - 0.0894841775 * a - 1.291485548 * b) ** 3;
  const linear = [
    4.0767416621 * l_ - 3.3077115913 * m_ + 0.2309699292 * s_,
    -1.2684380046 * l_ + 2.6097574011 * m_ - 0.3413193965 * s_,
    -0.0041960863 * l_ - 0.7034186147 * m_ + 1.707614701 * s_,
  ];
  return linear.map((v) => {
    const clipped = Math.min(1, Math.max(0, v));
    return clipped <= 0.0031308 ? 12.92 * clipped : 1.055 * clipped ** (1 / 2.4) - 0.055;
  }) as Rgb;
}

/** `color` at `alpha` over `backdrop`, composited the way a browser does (in gamma-encoded sRGB). */
function over(color: Rgb, alpha: number, backdrop: Rgb): Rgb {
  return color.map((v, i) => v * alpha + backdrop[i] * (1 - alpha)) as Rgb;
}

function luminance(rgb: Rgb): number {
  const [r, g, b] = rgb.map((v) => (v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrast(a: Rgb, b: Rgb): number {
  const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (light + 0.05) / (dark + 0.05);
}

const WHITE: Rgb = [1, 1, 1];

describe.each([
  ["light", ":root"],
  ["dark", ".dark"],
])("the %s theme", (_name, selector) => {
  const scope = block(selector);
  const background = token(scope, "background");

  it("muted text is readable on the page and on muted surfaces (4.5:1)", () => {
    const mutedText = token(scope, "muted-foreground");

    expect(contrast(mutedText, background)).toBeGreaterThanOrEqual(4.5);
    expect(contrast(mutedText, token(scope, "muted"))).toBeGreaterThanOrEqual(4.5);
    expect(contrast(mutedText, token(scope, "accent"))).toBeGreaterThanOrEqual(4.5);
  });

  it("the focus ring stands out from the page (3:1, WCAG 1.4.11 and 2.4.13)", () => {
    expect(contrast(token(scope, "ring"), background)).toBeGreaterThanOrEqual(3);
  });

  it("error text is readable on the tinted alert backgrounds (4.5:1)", () => {
    const destructive = token(scope, "destructive");

    for (const alpha of [0.05, 0.1]) {
      expect(contrast(destructive, over(destructive, alpha, background))).toBeGreaterThanOrEqual(4.5);
    }
  });

  it("muted help text is readable on the tinted alert background too", () => {
    const destructive = token(scope, "destructive");

    expect(contrast(token(scope, "muted-foreground"), over(destructive, 0.05, background))).toBeGreaterThanOrEqual(4.5);
  });
});

describe("the light theme buttons", () => {
  const scope = block(":root");

  it("white text on the destructive button is readable (4.5:1)", () => {
    expect(contrast(WHITE, token(scope, "destructive"))).toBeGreaterThanOrEqual(4.5);
  });

  it("the focus ring is visible against a dark primary button: it is drawn on the page, 2px outside it", () => {
    const ring = token(scope, "ring");
    // The outline sits on the page behind the button (outline-offset: 2px), so it is judged against the page, not the button.
    expect(contrast(ring, token(scope, "background"))).toBeGreaterThanOrEqual(3);
    expect(css).toMatch(/:focus-visible\s*\{[^}]*outline:\s*2px solid var\(--ring\);[^}]*outline-offset:\s*2px/);
  });
});

describe("the focus rule", () => {
  it("is defined once, for every kind of interactive element", () => {
    for (const selector of ["a[href]", "button", "input", "select", "textarea", "summary", "[tabindex]"]) {
      expect(css).toContain(selector);
    }
  });
});

describe("the focus ring does not animate in", () => {
  it("the button transitions colours, not every property (which would fade the outline in from its default)", () => {
    const button = readFileSync(join(__dirname, "..", "components", "ui", "button.tsx"), "utf8");

    expect(button).not.toMatch(/transition-all/);
    expect(button).toContain("transition-[color,background-color,border-color,box-shadow,opacity]");
  });
});
